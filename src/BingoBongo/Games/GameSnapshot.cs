namespace BingoBongo.Games;

public record AutoPilotState(bool On, DateTimeOffset? NextDrawAt);

public record GameSnapshot(
    int Round,
    int NumberOfDrew,
    string? CurrentNumber,
    IReadOnlyDictionary<int, string> History,
    AutoPilotState AutoPilot,
    DateTimeOffset ServerTime,
    int ViewerCount,
    Guid? JoinCode);

public record DrawResult(bool GameOver, string? Ball, IReadOnlyDictionary<int, string> History);
