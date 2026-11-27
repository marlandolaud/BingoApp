using BingoBongo.Games;
using BingoBongo.Live;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BingoBongo;

/// <summary>
/// Public side of a game: viewers know only the JoinCode and their own ViewerId, never the dealer's GameKey.
/// </summary>
[ApiController]
public class ViewerController(GameStore games, ILogger<ViewerController> logger) : Controller
{
    [HttpPost("viewers")]
    public async Task<IActionResult> Join([FromQuery] Guid join, CancellationToken ct)
    {
        var viewerId = await games.JoinAsync(join, ct);
        return viewerId is null ? NotFound() : Ok(new { viewerId });
    }

    [HttpGet("watch/{viewerId:guid}/state")]
    public async Task<IActionResult> State(Guid viewerId, CancellationToken ct)
    {
        var viewer = await games.GetViewerSnapshotAsync(viewerId, ct);
        return viewer is null ? NotFound() : Ok(viewer.Value.Snapshot);
    }

    [HttpGet("watch/{viewerId:guid}/events")]
    public async Task<IActionResult> Events(
        Guid viewerId,
        [FromServices] GameEventHub hub,
        [FromServices] IServiceScopeFactory scopes,
        [FromServices] IOptions<LiveOptions> live,
        CancellationToken ct)
    {
        var viewer = await games.GetViewerSnapshotAsync(viewerId, ct);
        if (viewer is null)
        {
            return NotFound();
        }

        // The stream outlives the request's DbContext, so every call gets a fresh scope.
        async Task<GameSnapshot?> Load(CancellationToken token)
        {
            await using var scope = scopes.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<GameStore>()
                .GetViewerSnapshotAsync(viewerId, token))?.Snapshot;
        }

        async Task Presence(SsePresence presence, CancellationToken token)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<GameStore>()
                    .UpdatePresenceAsync(viewerId, presence, token);
            }
            catch (Exception ex) when (presence == SsePresence.Disconnected)
            {
                // Runs in the stream's finally; it must not mask the original exception.
                logger.LogWarning(ex, "Could not record viewer {ViewerId} disconnecting", viewerId);
            }
        }

        await SseStream.RunAsync(HttpContext, viewer.Value.GameId, Load, Presence, hub,
            TimeSpan.FromSeconds(live.Value.HeartbeatSeconds));
        return new EmptyResult();
    }
}
