namespace ShutkiVorta.Domain.Wholesale;

/// <summary>Days of the week a restaurant wants deliveries, stored as a bit mask.</summary>
[Flags]
public enum WeekDays
{
    None = 0,
    Monday = 1,
    Tuesday = 2,
    Wednesday = 4,
    Thursday = 8,
    Friday = 16,
    Saturday = 32,
    Sunday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    EveryDay = Weekdays | Saturday | Sunday,
}

public static class WeekDaysExtensions
{
    /// <summary>Monday-first order, the way restaurants plan their week.</summary>
    public static readonly IReadOnlyList<DayOfWeek> WeekOrder =
    [
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
    ];

    public static WeekDays ToWeekDays(this DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => WeekDays.Monday,
        DayOfWeek.Tuesday => WeekDays.Tuesday,
        DayOfWeek.Wednesday => WeekDays.Wednesday,
        DayOfWeek.Thursday => WeekDays.Thursday,
        DayOfWeek.Friday => WeekDays.Friday,
        DayOfWeek.Saturday => WeekDays.Saturday,
        DayOfWeek.Sunday => WeekDays.Sunday,
        _ => WeekDays.None,
    };

    public static WeekDays Combine(IEnumerable<DayOfWeek> days) =>
        days.Aggregate(WeekDays.None, (all, day) => all | day.ToWeekDays());

    public static bool Includes(this WeekDays days, DayOfWeek day) => (days & day.ToWeekDays()) != 0;

    public static IReadOnlyList<DayOfWeek> ToDays(this WeekDays days) => WeekOrder.Where(d => days.Includes(d)).ToList();

    public static int CountDays(this WeekDays days) => WeekOrder.Count(d => days.Includes(d));

    public static string Describe(this WeekDays days) => days switch
    {
        WeekDays.None => "No days",
        WeekDays.EveryDay => "Every day",
        WeekDays.Weekdays => "Weekdays (Mon–Fri)",
        WeekDays.Saturday | WeekDays.Sunday => "Weekends",
        _ => string.Join(", ", days.ToDays().Select(d => d.ToString()[..3])),
    };
}
