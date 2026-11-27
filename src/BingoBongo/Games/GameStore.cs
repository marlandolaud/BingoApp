using BingoBongo.Data;
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

    public async Task<DrawResult> DrawAsync(Guid gameKey, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var game = await LockGameAsync(gameKey, ct);
        var called = await CalledBallsAsync(gameKey, ct);

        var ball = BallPicker.Next(called, Random.Shared);
        if (ball is null)
        {
            await tx.CommitAsync(ct);
            return new DrawResult(true, null, ToHistory(called));
        }

        db.Draws.Add(new DrawEntity { GameId = gameKey, Seq = called.Count, Ball = ball });
        game.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await NotifyAsync(gameKey, ct);
        await tx.CommitAsync(ct);

        called.Add(ball);
        return new DrawResult(false, ball, ToHistory(called));
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

    public async Task<GameSnapshot> GetDealerSnapshotAsync(Guid gameKey, CancellationToken ct)
    {
        await EnsureGameAsync(gameKey, ct);
        var game = await db.Games.AsNoTracking().SingleAsync(g => g.Id == gameKey, ct);
        var called = await CalledBallsAsync(gameKey, ct);
        var now = clock.GetUtcNow();
        var seenSince = now - TimeSpan.FromSeconds(2 * _live.HeartbeatSeconds);
        var viewerCount = await db.Viewers.CountAsync(v => v.GameId == gameKey && v.LastSeenAt > seenSince, ct);

        return new GameSnapshot(
            game.Round,
            called.Count,
            called.Count > 0 ? called[^1] : null,
            ToHistory(called),
            new AutoPilotState(game.AutoPilotNextDrawAt != null, game.AutoPilotNextDrawAt),
            now,
            viewerCount,
            game.JoinCode);
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

    private static Dictionary<int, string> ToHistory(IReadOnlyList<string> called) =>
        called.Select((ball, i) => (ball, i)).ToDictionary(x => x.i, x => x.ball);
}
