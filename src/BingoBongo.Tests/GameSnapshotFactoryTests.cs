using BingoBongo.Data;
using BingoBongo.Games;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class GameSnapshotFactoryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20000);
    private static readonly Guid Join = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private static GameEntity Game(DateTimeOffset? next = null, int round = 3) =>
        new() { Id = Guid.NewGuid(), JoinCode = Join, Round = round, AutoPilotNextDrawAt = next };

    [Fact]
    public void Empty_game_has_no_current_number_and_empty_history()
    {
        var s = GameSnapshotFactory.Create(Game(), [], 0, Now, includeJoinCode: true);

        Assert.Equal(3, s.Round);
        Assert.Equal(0, s.NumberOfDrew);
        Assert.Null(s.CurrentNumber);
        Assert.Empty(s.History);
    }

    [Fact]
    public void Current_number_is_the_last_called_and_history_is_in_call_order()
    {
        var s = GameSnapshotFactory.Create(Game(), ["B7", "N31", "G50"], 0, Now, includeJoinCode: true);

        Assert.Equal(3, s.NumberOfDrew);
        Assert.Equal("G50", s.CurrentNumber);
        Assert.Equal(new Dictionary<int, string> { [0] = "B7", [1] = "N31", [2] = "G50" }, s.History);
    }

    [Fact]
    public void Single_ball_is_the_current_number()
    {
        var s = GameSnapshotFactory.Create(Game(), ["O70"], 0, Now, includeJoinCode: false);
        Assert.Equal("O70", s.CurrentNumber);
        Assert.Equal(1, s.NumberOfDrew);
    }

    [Fact]
    public void Auto_pilot_is_on_with_its_next_draw_time()
    {
        var next = Now.AddSeconds(7);
        var s = GameSnapshotFactory.Create(Game(next), [], 0, Now, includeJoinCode: true);
        Assert.Equal(new AutoPilotState(true, next), s.AutoPilot);
    }

    [Fact]
    public void Auto_pilot_is_off_without_a_next_draw_time()
    {
        var s = GameSnapshotFactory.Create(Game(), [], 0, Now, includeJoinCode: true);
        Assert.Equal(new AutoPilotState(false, null), s.AutoPilot);
    }

    [Fact]
    public void Carries_server_time_and_viewer_count()
    {
        var s = GameSnapshotFactory.Create(Game(), [], 4, Now, includeJoinCode: true);
        Assert.Equal(Now, s.ServerTime);
        Assert.Equal(4, s.ViewerCount);
    }

    [Fact]
    public void Dealer_snapshot_includes_the_join_code() =>
        Assert.Equal(Join, GameSnapshotFactory.Create(Game(), [], 0, Now, includeJoinCode: true).JoinCode);

    [Fact]
    public void Viewer_snapshot_hides_the_join_code() =>
        Assert.Null(GameSnapshotFactory.Create(Game(), [], 0, Now, includeJoinCode: false).JoinCode);

    [Fact]
    public void History_maps_call_order_to_ball() =>
        Assert.Equal(new Dictionary<int, string> { [0] = "I20", [1] = "B1" }, GameSnapshotFactory.History(["I20", "B1"]));
}
