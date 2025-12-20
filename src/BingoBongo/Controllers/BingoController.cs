using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace BingoBongo;

[ApiController]
public class BingoController : Controller
{
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<BingoController> _logger;
    private readonly Random _rand;

    public BingoController(IMemoryCache memoryCache, ILogger<BingoController> logger)
    {
        _memoryCache = memoryCache;
        _logger = logger;
        _rand =  new Random(DateTime.Now.Millisecond);
    }

    [HttpPost("Reset")]
    public IActionResult Reset([FromQuery]  Guid GameKey)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }
        
        _memoryCache.Set(GameKey, new Dictionary<int,string>());
        return Ok();
    }
    
    
    [HttpGet("GetGameKey")]
    public IActionResult GetGameKey()
    {
        return Ok(Guid.NewGuid());
    }
    
    [HttpGet("Get")]
    public IActionResult Index([FromQuery] Guid GameKey)
    {
        if (GameKey == Guid.Empty)
        {
            return BadRequest("GameKey is empty");
        }
        
        _memoryCache.TryGetValue(GameKey, out Dictionary<int,string> myBingoHistory);
        if (myBingoHistory == null)
        {
            _logger.LogInformation("No bingo history found");
            myBingoHistory = new Dictionary<int,string>();
        }

        var numberOfDraws = myBingoHistory.Count;
        if (numberOfDraws >= 75)
        {
            return Ok("Game Over!");
        }

        string bingoSelection = null;
        int count = 0;
        do
        {
            var next = _rand.Next(1, 76);
            bingoSelection = next switch
            {
                >= 1 and <= 15 => $"B{next}",
                >= 16 and <= 30 => $"I{next}",
                >= 31 and <= 45 => $"N{next}",
                >= 46 and <= 60 => $"G{next}",
                >= 61 and <= 75 => $"O{next}",
                _ => throw new ArgumentOutOfRangeException()
            };
            count++;
        } while (myBingoHistory.Values.Contains(bingoSelection));
        _logger.LogInformation("bingoSelection:{bingoSelection} count:{count}",  bingoSelection, count);
        
        myBingoHistory.Add(myBingoHistory.Count, bingoSelection);
        _memoryCache.Set(GameKey, myBingoHistory);

        var response = new ResponseDto()
        {
            NumberOfDrew = myBingoHistory.Count,
            CurrentNumber = bingoSelection,
            History = myBingoHistory
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

