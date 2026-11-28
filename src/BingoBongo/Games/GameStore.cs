using BingoBongo.Data;
using BingoBongo.Live;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BingoBongo.Games;

/// <summary>
/// Postgres-backed game state. Every write runs in a transaction that holds a
/// row lock on the game (SELECT ... FOR UPDATE), so concurrent draws on one game
/// are serialized, and sends a pg_notify just before commit.
/// </summary>
public class GameStore(
    BingoDbContext db,
    TimeProvider clock,
    IOptions<AutoPilotOptions> autoPilot,
    IOptions<LiveOptions> live)
{
    private readonly AutoPilotOptions _autoPilot = autoPilot.Value;
    private readonly LiveOptions _live = live.Value;
    private TimeSpan Interval => TimeSpan.FromSeconds(_autoPilot.IntervalSeconds);

    /// <summary>
    /// Draws the next ball. With <paramref name="due"/> (an auto pilot draw), draws only while
    /// that is still the scheduled time; otherwise returns the current state without drawing.
    /// </summary>
    public async Task<DrawResult> DrawAsync(Guid gameKey, CancellationToken ct, DateTimeOffset? due = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGameAsync(gameKey, ct);
        var called = await CalledBallsAsync(gameKey, ct);

        if (!AutoPilotSchedule.IsDue(game.AutoPilotNextDrawAt, due))
        {
            await tx.CommitAsync(ct);
            return new DrawResult(false, called.Count > 0 ? called[^1] : null, GameSnapshotFactory.History(called), Skipped: true);
        }

        var ball = BallPicker.Next(called, Random.Shared);
        if (ball is null)
        {
            await tx.CommitAsync(ct);
            return new DrawResult(true, null, GameSnapshotFactory.History(called));
        }

        db.Draws.Add(new DrawEntity { GameId = gameKey, Seq = called.Count, Ball = ball });
        var now = clock.GetUtcNow();
        game.AutoPilotNextDrawAt = AutoPilotSchedule.AfterDraw(game.AutoPilotNextDrawAt, called.Count + 1, now, Interval);
        game.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await NotifyAsync(gameKey, ct);
        await tx.CommitAsync(ct);

        called.Add(ball);
        return new DrawResult(false, ball, GameSnapshotFactory.History(called));
    }

    public async Task ResetAsync(Guid gameKey, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGameAsync(gameKey, ct);

        await db.Draws.Where(d => d.GameId == gameKey).ExecuteDeleteAsync(ct);
        game.Round++;
        game.AutoPilotNextDrawAt = null;
        game.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyAsync(gameKey, ct);
        await tx.CommitAsync(ct);
    }

    public async Task SetAutoPilotAsync(Guid gameKey, bool on, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGameAsync(gameKey, ct);
        var drawCount = await db.Draws.CountAsync(d => d.GameId == gameKey, ct);

        var now = clock.GetUtcNow();
        game.AutoPilotNextDrawAt = AutoPilotSchedule.Toggle(on, drawCount, now, Interval);
        game.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await NotifyAsync(gameKey, ct);
        await tx.CommitAsync(ct);
    }

    public async Task<GameSnapshot> GetDealerSnapshotAsync(Guid gameKey, CancellationToken ct)
    {
        await EnsureGameAsync(gameKey, ct);
        var game = await db.Games.AsNoTracking().SingleAsync(g => g.Id == gameKey, ct);
        return await BuildSnapshotAsync(game, includeJoinCode: true, ct);
    }

    /// <summary>A new viewer for the game with this public join code, or null if there is none.</summary>
    public async Task<Guid?> JoinAsync(Guid joinCode, CancellationToken ct)
    {
        var gameId = await db.Games.AsNoTracking()
            .Where(g => g.JoinCode == joinCode)
            .Select(g => (Guid?)g.Id)
            .SingleOrDefaultAsync(ct);
        if (gameId is null)
        {
            return null;
        }

        var viewer = new ViewerEntity { Id = Guid.NewGuid(), GameId = gameId.Value, CreatedAt = clock.GetUtcNow() };
        db.Viewers.Add(viewer);
        await db.SaveChangesAsync(ct);
        return viewer.Id;
    }

    /// <summary>
    /// The viewer's game id (the hub key; never expose it to viewers) and the snapshot
    /// without the join code, or null for an unknown viewer.
    /// </summary>
    public async Task<(Guid GameId, GameSnapshot Snapshot)?> GetViewerSnapshotAsync(Guid viewerId, CancellationToken ct)
    {
        var game = await GameOfViewerAsync(viewerId, ct);
        if (game is null)
        {
            return null;
        }
        return (game.Id, await BuildSnapshotAsync(game, includeJoinCode: false, ct));
    }

    public async Task UpdatePresenceAsync(Guid viewerId, SsePresence presence, CancellationToken ct)
    {
        DateTimeOffset? seen = presence == SsePresence.Disconnected ? null : clock.GetUtcNow();
        var updated = await db.Viewers.Where(v => v.Id == viewerId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.LastSeenAt, seen), ct);
        if (updated == 0 || presence == SsePresence.Heartbeat)
        {
            return;
        }

        var gameId = await db.Viewers.AsNoTracking().Where(v => v.Id == viewerId).Select(v => v.GameId).SingleAsync(ct);
        await NotifyAsync(gameId, ct);
    }

    private Task<GameEntity?> GameOfViewerAsync(Guid viewerId, CancellationToken ct) =>
        db.Viewers.AsNoTracking()
            .Where(v => v.Id == viewerId)
            .Select(v => db.Games.First(g => g.Id == v.GameId))
            .SingleOrDefaultAsync(ct);

    private async Task<GameSnapshot> BuildSnapshotAsync(GameEntity game, bool includeJoinCode, CancellationToken ct)
    {
        var called = await CalledBallsAsync(game.Id, ct);
        var now = clock.GetUtcNow();
        var seenSince = ViewerPresence.WatchingSince(now, _live.HeartbeatSeconds);
        var viewerCount = await db.Viewers.CountAsync(v => v.GameId == game.Id && v.LastSeenAt > seenSince, ct);
        return GameSnapshotFactory.Create(game, called, viewerCount, now, includeJoinCode);
    }

    /// <summary>Creates the game row if missing, then locks it. Must run inside a transaction.</summary>
    private async Task<GameEntity> LockGameAsync(Guid gameKey, CancellationToken ct)
    {
        await EnsureGameAsync(gameKey, ct);
        var rows = await db.Games
            .FromSqlInterpolated($"SELECT * FROM games WHERE id = {gameKey} FOR UPDATE")
            .ToListAsync(ct);
        return rows.Single();
    }

    // ON CONFLICT DO NOTHING: a second concurrent insert waits on the first transaction's
    // uncommitted row and then does nothing, so the following FOR UPDATE finds the row.
    private Task EnsureGameAsync(Guid gameKey, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO games (id, join_code, round, created_at, updated_at) VALUES ({gameKey}, {Guid.NewGuid()}, 1, {now}, {now}) ON CONFLICT (id) DO NOTHING",
            ct);
    }

    private Task<List<string>> CalledBallsAsync(Guid gameKey, CancellationToken ct) =>
        db.Draws.AsNoTracking()
            .Where(d => d.GameId == gameKey)
            .OrderBy(d => d.Seq)
            .Select(d => d.Ball)
            .ToListAsync(ct);

    private Task NotifyAsync(Guid gameKey, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("SELECT pg_notify('game_events', {0})", [gameKey.ToString()], ct);
}
