using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Features.Orders;
using ShutkiVorta.UnitTests.TestDoubles;

namespace ShutkiVorta.UnitTests.Application;

public sealed class OrderScheduleTests
{
    // Thursday 2026-10-08 15:00 business time.
    private static readonly DateTime Now = new(2026, 10, 8, 15, 0, 0);

    private static OrderSchedule Create(Action<OrderingOptions>? configure = null)
    {
        var options = new OrderingOptions
        {
            MinimumLeadTimeHours = 24,
            MaxDaysInAdvance = 7,
            FirstSlot = "11:00",
            LastSlot = "19:00",
            SlotIntervalMinutes = 60,
            ClosedDays = [DayOfWeek.Monday],
        };
        configure?.Invoke(options);
        return new OrderSchedule(Options.Create(options), new FakeClock(Now));
    }

    [Fact]
    public void DailySlots_SpanFirstToLastSlot()
    {
        var slots = Create().DailySlots();

        Assert.Equal(9, slots.Count);
        Assert.Equal(new TimeOnly(11, 0), slots[0]);
        Assert.Equal(new TimeOnly(19, 0), slots[^1]);
    }

    [Fact]
    public void GetAvailableDays_RespectsLeadTime()
    {
        var days = Create().GetAvailableDays();

        // Today is excluded entirely; tomorrow only from 15:00 (24h lead time).
        Assert.Equal(new DateOnly(2026, 10, 9), days[0].Date);
        Assert.Equal(new TimeOnly(15, 0), days[0].Slots[0]);
        Assert.Equal(new TimeOnly(11, 0), days[1].Slots[0]);
    }

    [Fact]
    public void GetAvailableDays_SkipsClosedDays()
    {
        var days = Create().GetAvailableDays();
        Assert.DoesNotContain(days, d => d.Date.DayOfWeek == DayOfWeek.Monday);
    }

    [Fact]
    public void Validate_RejectsTooSoon_ClosedDayAndOddSlots()
    {
        var schedule = Create();

        Assert.NotNull(schedule.Validate(new DateOnly(2026, 10, 9), new TimeOnly(11, 0)));   // inside 24h lead time
        Assert.NotNull(schedule.Validate(new DateOnly(2026, 10, 12), new TimeOnly(12, 0)));  // Monday
        Assert.NotNull(schedule.Validate(new DateOnly(2026, 10, 10), new TimeOnly(12, 30))); // not a slot
        Assert.Null(schedule.Validate(new DateOnly(2026, 10, 10), new TimeOnly(12, 0)));
    }
}
