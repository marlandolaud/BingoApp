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

[Trait("Category", "Unit")]
public class AutoPilotScheduleIsDueTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(20000).AddTicks(1234560);

    [Fact]
    public void A_draw_without_a_target_is_always_due()
    {
        Assert.True(AutoPilotSchedule.IsDue(null, null));
        Assert.True(AutoPilotSchedule.IsDue(At, null));
    }

    [Fact]
    public void The_scheduled_target_is_due() =>
        Assert.True(AutoPilotSchedule.IsDue(At, At));

    [Fact]
    public void A_target_in_another_offset_is_the_same_instant() =>
        Assert.True(AutoPilotSchedule.IsDue(At, At.ToOffset(TimeSpan.FromHours(2))));

    [Fact]
    public void Sub_microsecond_differences_are_ignored_like_postgres() =>
        Assert.True(AutoPilotSchedule.IsDue(At, At.AddTicks(5)));

    [Fact]
    public void A_one_microsecond_difference_is_stale()
    {
        Assert.False(AutoPilotSchedule.IsDue(At, At.AddTicks(10)));
        Assert.False(AutoPilotSchedule.IsDue(At, At.AddTicks(-10)));
    }

    [Fact]
    public void A_target_when_auto_pilot_is_off_is_stale() =>
        Assert.False(AutoPilotSchedule.IsDue(null, At));
}
