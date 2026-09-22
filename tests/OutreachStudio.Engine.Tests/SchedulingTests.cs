using OutreachStudio.Engine.Scheduling;
using static OutreachStudio.Engine.Tests.Fixtures;

namespace OutreachStudio.Engine.Tests;

public class SchedulingTests
{
    private static readonly Schedule Quiet22To8 = new(Now, 600, 22, 8, 0.1);

    [Fact]
    public void Send_outside_quiet_hours_is_not_moved()
    {
        // 12:00 UTC is 14:00 in Warsaw.
        Assert.Equal(Now, QuietHours.NextAllowed(Now, "Europe/Warsaw", Quiet22To8));
    }

    [Fact]
    public void Send_inside_quiet_hours_moves_to_the_next_morning_in_the_users_zone()
    {
        // 21:30 UTC is 23:30 in Warsaw, quiet. Next allowed is 08:00 Warsaw, which is 06:00 UTC.
        var at = new DateTimeOffset(2026, 9, 22, 21, 30, 0, TimeSpan.Zero);
        var moved = QuietHours.NextAllowed(at, "Europe/Warsaw", Quiet22To8);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 6, 0, 0, TimeSpan.Zero), moved);

        // The same instant is 17:30 in New York, allowed.
        Assert.Equal(at, QuietHours.NextAllowed(at, "America/New_York", Quiet22To8));
    }

    [Fact]
    public void Early_morning_inside_quiet_hours_moves_to_the_same_morning()
    {
        // 03:00 UTC is 05:00 in Warsaw, quiet until 08:00 the same day.
        var at = new DateTimeOffset(2026, 9, 22, 3, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 6, 0, 0, TimeSpan.Zero), QuietHours.NextAllowed(at, "Europe/Warsaw", Quiet22To8));
    }

    [Fact]
    public void Unknown_time_zone_falls_back_to_utc()
    {
        var at = new DateTimeOffset(2026, 9, 22, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero), QuietHours.NextAllowed(at, "Mars/Olympus", Quiet22To8));
    }

    [Fact]
    public void Holdout_is_deterministic_and_close_to_the_share()
    {
        var campaign = Guid.Parse("6f1d3d2e-3b8a-4f4e-9c9a-2a1b8c7d6e5f");
        var first = Enumerable.Range(1, 20_000).Count(id => Holdout.Contains(campaign, id, 0.1));
        var second = Enumerable.Range(1, 20_000).Count(id => Holdout.Contains(campaign, id, 0.1));

        Assert.Equal(first, second);
        Assert.InRange(first, 1_800, 2_200);
        Assert.Equal(0, Enumerable.Range(1, 1000).Count(id => Holdout.Contains(campaign, id, 0)));
        Assert.NotEqual(first, Enumerable.Range(1, 20_000).Count(id => Holdout.Contains(Guid.NewGuid(), id, 0.1) && Holdout.Contains(campaign, id, 0.1)));
    }

    [Fact]
    public void Plan_spreads_users_at_the_throttle_rate_and_marks_holdout()
    {
        var users = Enumerable.Range(1, 120).Select(i => User(id: i, timeZone: "America/New_York")).ToList();
        var schedule = new Schedule(Now, 60, 22, 22, 0.25);

        var plan = RolloutPlanner.Plan(Guid.NewGuid(), users, schedule);

        Assert.Equal(120, plan.Count);
        Assert.Equal(Now, plan[0].DueAt);
        Assert.Equal(Now.AddMinutes(1), plan[60].DueAt);
        Assert.Equal(Now.AddSeconds(119), plan[119].DueAt);
        Assert.InRange(plan.Count(p => p.Holdout), 15, 45);

        var histogram = RolloutPlanner.Histogram(plan, Now, 1);
        Assert.Equal(2, histogram.Count);
        Assert.Equal(plan.Count(p => !p.Holdout), histogram.Sum(h => h.Count));
    }
}
