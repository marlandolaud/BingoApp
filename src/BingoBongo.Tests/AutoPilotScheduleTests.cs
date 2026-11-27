using BingoBongo.Games;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class AutoPilotScheduleTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddSeconds(100);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    [Fact]
    public void AfterDraw_off_stays_off() =>
        Assert.Null(AutoPilotSchedule.AfterDraw(null, 5, Now, Interval));

    [Fact]
    public void AfterDraw_on_moves_to_now_plus_interval() =>
        Assert.Equal(Now.AddSeconds(10), AutoPilotSchedule.AfterDraw(Now.AddSeconds(-1), 5, Now, Interval));

    [Fact]
    public void AfterDraw_at_75_turns_off() =>
        Assert.Null(AutoPilotSchedule.AfterDraw(Now, 75, Now, Interval));

    [Fact]
    public void AfterDraw_at_74_stays_on() =>
        Assert.Equal(Now.AddSeconds(10), AutoPilotSchedule.AfterDraw(Now, 74, Now, Interval));

    [Fact]
    public void Toggle_on_with_no_draws_schedules() =>
        Assert.Equal(Now.AddSeconds(10), AutoPilotSchedule.Toggle(true, 0, Now, Interval));

    [Fact]
    public void Toggle_off_clears() =>
        Assert.Null(AutoPilotSchedule.Toggle(false, 0, Now, Interval));

    [Fact]
    public void Toggle_on_when_game_over_stays_off() =>
        Assert.Null(AutoPilotSchedule.Toggle(true, 75, Now, Interval));
}
