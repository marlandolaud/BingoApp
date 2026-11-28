using System.Text;
using BingoBongo.Games;
using BingoBongo.Live;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BingoBongo;

[ApiController]
public class BingoController : Controller
{
    private readonly GameStore _games;
    private readonly ILogger<BingoController> _logger;

    public BingoController(GameStore games, ILogger<BingoController> logger)
    {
        _games = games;
        _logger = logger;
    }

    [HttpPost("Reset")]
    public async Task<IActionResult> Reset([FromQuery] Guid GameKey, CancellationToken ct)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        await _games.ResetAsync(GameKey, ct);
        return Ok();
    }
    
    
    [HttpPost("AutoPilot")]
    public async Task<IActionResult> AutoPilot([FromQuery] Guid GameKey, [FromQuery] bool on, CancellationToken ct)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        await _games.SetAutoPilotAsync(GameKey, on, ct);
        return NoContent();
    }

    [HttpGet("GetGameKey")]
    public IActionResult GetGameKey()
    {
        return Ok(Guid.NewGuid());
    }

    [HttpGet("State")]
    public async Task<IActionResult> State([FromQuery] Guid GameKey, CancellationToken ct)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        return Ok(await _games.GetDealerSnapshotAsync(GameKey, ct));
    }
    
    [HttpGet("Events")]
    public async Task<IActionResult> Events(
        [FromQuery] Guid GameKey,
        [FromServices] GameEventHub hub,
        [FromServices] IServiceScopeFactory scopes,
        [FromServices] IOptions<LiveOptions> live)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        // The stream outlives any one DbContext's useful life, so each load gets a fresh scope.
        async Task<GameSnapshot?> Load(CancellationToken ct)
        {
            await using var scope = scopes.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<GameStore>().GetDealerSnapshotAsync(GameKey, ct);
        }

        await SseStream.RunAsync(HttpContext, GameKey, Load, presence: null, hub,
            TimeSpan.FromSeconds(live.Value.HeartbeatSeconds));
        return new EmptyResult();
    }

    [HttpGet("Get")]
    // `due`: an auto pilot draw names the nextDrawAt it is for; if that is no longer the
    // scheduled time (another dealer tab already drew it), nothing is drawn and the current
    // state comes back in the same shape.
    public async Task<IActionResult> Index([FromQuery] Guid GameKey, [FromQuery] DateTimeOffset? due, CancellationToken ct)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        var draw = await _games.DrawAsync(GameKey, ct, due);
        if (draw.GameOver)
        {
            return Ok("Game Over!");
        }

        if (!draw.Skipped)
        {
            _logger.LogInformation("bingoSelection:{bingoSelection}", draw.Ball);
        }

        var response = new ResponseDto()
        {
            NumberOfDrew = draw.History.Count,
            CurrentNumber = draw.Ball ?? "",
            History = new Dictionary<int, string>(draw.History)
        };
        
        return Ok(response);
    }

    public class ResponseDto
    {
        public required int  NumberOfDrew { get; set; }
        public required string CurrentNumber { get; set; }

        public required Dictionary<int, string> History { get; set; }
        
        public string OrderedDrawHistory
        {
            get
            {
                return string.Join(",", History
                    .OrderByDescending(kvp => kvp.Key)
                    .Select(kvp => kvp.Value));
            }
        }
        
        public string OrderedBingo
        {
            get
            {
                var bingo = "BINGO";
                var o = new StringBuilder();
                foreach (var c in bingo)
                {
                    o.AppendLine(string.Join(",", History
                        .Where(kvp => kvp.Value.Contains(c))
                        .OrderBy(kvp => int.Parse(kvp.Value.Remove(0,1)))
                        .Select(kvp => kvp.Value)));
                }
                return o.ToString();
            }
        }
    }
}

