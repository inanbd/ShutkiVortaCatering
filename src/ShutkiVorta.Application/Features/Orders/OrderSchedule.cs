using System.Globalization;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Features.Orders;

public sealed record ScheduleDay(DateOnly Date, IReadOnlyList<TimeOnly> Slots)
{
    public string Label => Date.ToString("dddd, MMM d", Format.Culture);
    public string Value => Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Works out which pickup/delivery dates and time slots a customer can choose, in Dallas local time.</summary>
public sealed class OrderSchedule(IOptions<OrderingOptions> options, IDateTimeProvider clock)
{
    public IReadOnlyList<TimeOnly> DailySlots()
    {
        var o = options.Value;
        return BuildSlots(o.FirstSlot, o.LastSlot, o.SlotIntervalMinutes, new TimeOnly(11, 0), new TimeOnly(19, 0));
    }

    /// <summary>Evenly spaced time slots from <paramref name="firstSlot"/> to <paramref name="lastSlot"/> inclusive.</summary>
    public static IReadOnlyList<TimeOnly> BuildSlots(string? firstSlot, string? lastSlot, int intervalMinutes, TimeOnly defaultFirst, TimeOnly defaultLast)
    {
        var first = ParseTime(firstSlot, defaultFirst);
        var last = ParseTime(lastSlot, defaultLast);
        var interval = Math.Clamp(intervalMinutes, 15, 240);

        var slots = new List<TimeOnly>();
        for (var t = first; t <= last; t = t.AddMinutes(interval))
        {
            slots.Add(t);
            if (t.AddMinutes(interval) < t)
            {
                break; // wrapped past midnight
            }
        }

        return slots;
    }

    public IReadOnlyList<ScheduleDay> GetAvailableDays()
    {
        var o = options.Value;
        var now = clock.BusinessNow;
        var earliest = now.AddHours(Math.Max(0, o.MinimumLeadTimeHours));
        var slots = DailySlots();
        var today = DateOnly.FromDateTime(now);
        var days = new List<ScheduleDay>();

        for (var offset = 0; offset <= Math.Max(1, o.MaxDaysInAdvance); offset++)
        {
            var date = today.AddDays(offset);
            if (o.IsKitchenClosed(date))
            {
                continue;
            }

            var available = slots.Where(s => date.ToDateTime(s) >= earliest).ToList();
            if (available.Count > 0)
            {
                days.Add(new ScheduleDay(date, available));
            }
        }

        return days;
    }

    /// <summary>Returns a customer-facing error if the requested time cannot be booked, otherwise <c>null</c>.</summary>
    public string? Validate(DateOnly date, TimeOnly time)
    {
        var day = GetAvailableDays().FirstOrDefault(d => d.Date == date);
        if (day is null)
        {
            return options.Value.IsKitchenClosed(date)
                ? $"Our kitchen is closed on {date.ToString("dddd, MMM d", Format.Culture)}. Please choose another date."
                : $"Please choose a date at least {options.Value.MinimumLeadTimeHours} hours from now and within the next {options.Value.MaxDaysInAdvance} days.";
        }

        return day.Slots.Contains(time) ? null : "Please choose one of the available time slots.";
    }

    private static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : fallback;
}
