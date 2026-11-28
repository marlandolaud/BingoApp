using BingoBongo.Data;

namespace BingoBongo.Games;

/// <summary>Builds the snapshot clients see from a game row and its called balls.</summary>
public static class GameSnapshotFactory
{
    public static GameSnapshot Create(
        GameEntity game, IReadOnlyList<string> calledInOrder, int viewerCount, DateTimeOffset now, bool includeJoinCode) =>
        new(
            game.Round,
            calledInOrder.Count,
            calledInOrder.Count > 0 ? calledInOrder[^1] : null,
            History(calledInOrder),
            new AutoPilotState(game.AutoPilotNextDrawAt != null, game.AutoPilotNextDrawAt),
            now,
            viewerCount,
            includeJoinCode ? game.JoinCode : null);

    /// <summary>Call order (0-based) to ball.</summary>
    public static Dictionary<int, string> History(IReadOnlyList<string> calledInOrder) =>
        calledInOrder.Select((ball, i) => (ball, i)).ToDictionary(x => x.i, x => x.ball);
}
