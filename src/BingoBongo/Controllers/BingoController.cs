using System.Text;
using BingoBongo.Games;
using Microsoft.AspNetCore.Mvc;

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
    
    [HttpGet("Get")]
    public async Task<IActionResult> Index([FromQuery] Guid GameKey, CancellationToken ct)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }

        var draw = await _games.DrawAsync(GameKey, ct);
        if (draw.GameOver)
        {
            return Ok("Game Over!");
        }

        _logger.LogInformation("bingoSelection:{bingoSelection}", draw.Ball);

        var response = new ResponseDto()
        {
            NumberOfDrew = draw.History.Count,
            CurrentNumber = draw.Ball!,
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

