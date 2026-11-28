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

/// <summary>A draw's outcome; <c>Skipped</c> when an auto pilot draw was no longer due (Ball is then the last called, if any).</summary>
public record DrawResult(bool GameOver, string? Ball, IReadOnlyDictionary<int, string> History, bool Skipped = false);
