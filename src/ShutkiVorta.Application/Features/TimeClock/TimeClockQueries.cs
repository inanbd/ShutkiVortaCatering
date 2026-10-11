using System.Globalization;
using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>The caller's own time clock: their open shift and their shifts this week and last week.</summary>
public sealed record GetMyTimeClockQuery : IRequest<MyTimeClockDto>, IRequireStaff;

/// <summary>One shift; null (not found) unless it is the caller's own shift or they may see the team's shifts, so ids can't be probed.</summary>
public sealed record GetShiftQuery(int Id) : IRequest<ShiftDetailsDto?>, IRequireStaff;

/// <summary>Who is on the clock, how many shifts need a look, and the people to filter by.</summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed record GetTeamTimeClockQuery : IRequest<TeamTimeClockDto>;

/// <summary>Everyone's shifts by the day they started (newest first), with the hours inside the dates.</summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed record GetShiftsQuery : IRequest<ShiftListDto>
{
    public string? UserId { get; init; }

    /// <summary>First business day (null: no limit).</summary>
    public DateOnly? From { get; init; }

    /// <summary>Last business day, included (null: no limit).</summary>
    public DateOnly? To { get; init; }

    public ShiftFilter Filter { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

/// <summary>Hours per person per workweek for the workweeks that contain <see cref="From"/> to <see cref="To"/> (default: this week and last).</summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed record GetWeeklyHoursQuery : IRequest<WeeklyHoursDto>
{
    public string? UserId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

/// <summary>The shifts of <see cref="GetShiftsQuery"/> (all pages, oldest first) as a CSV file for payroll.</summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed record ExportShiftsCsvQuery : IRequest<TimeClockCsvFile>
{
    public string? UserId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public ShiftFilter Filter { get; init; }
}

/// <summary>The weekly hours of <see cref="GetWeeklyHoursQuery"/> as a CSV file.</summary>
[RequirePermission(Permissions.TimeClockViewAll)]
public sealed record ExportWeeklyHoursCsvQuery : IRequest<TimeClockCsvFile>
{
    public string? UserId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}

internal sealed class TimeClockQueryHandlers(
    ITimeClockRepository repository,
    IStaffDirectory directory,
    IStaffAuditLog audit,
    ICurrentStaff currentStaff,
    IDateTimeProvider clock,
    IOptions<TimeClockOptions> options) :
    IRequestHandler<GetMyTimeClockQuery, MyTimeClockDto>,
    IRequestHandler<GetShiftQuery, ShiftDetailsDto?>,
    IRequestHandler<GetTeamTimeClockQuery, TeamTimeClockDto>,
    IRequestHandler<GetShiftsQuery, ShiftListDto>,
    IRequestHandler<GetWeeklyHoursQuery, WeeklyHoursDto>,
    IRequestHandler<ExportShiftsCsvQuery, TimeClockCsvFile>,
    IRequestHandler<ExportWeeklyHoursCsvQuery, TimeClockCsvFile>
{
    public const int MaxExportRows = 20_000;
    public const int MaxWeeks = 26;
    private const int HistorySize = 200;

    private static readonly string[] ShiftColumns =
    [
        "Name", "Email", "Date", "Clock in", "Clock out", "Hours", "Status", "Clock-in location", "Clock-out location",
        "Clock-in distance (m)", "Clock-out distance (m)", "Clock-in coordinates", "Clock-out coordinates", "Edited by", "Reason", "Voided",
    ];

    private static readonly string[] WeeklyColumns =
    [
        "Week starting", "Week ending", "Name", "Email", "Hours", "Hours (h:mm)", "Over 40 h", "Shifts", "Shifts needing review", "On the clock now (not counted)",
    ];

    private TimeClockOptions Settings => options.Value;
    private TimeZoneInfo Zone => clock.BusinessTimeZone;
    private DateOnly Today => DateOnly.FromDateTime(clock.BusinessNow);

    public async Task<MyTimeClockDto> Handle(GetMyTimeClockQuery request, CancellationToken cancellationToken)
    {
        var userId = (await currentStaff.GetAsync(cancellationToken)).UserId ?? throw new InvalidOperationException("No signed-in staff member.");
        var now = clock.UtcNow;
        var thisWeek = BusinessTime.WeekStart(Today, Settings.WorkweekStartsOn);
        var lastWeek = thisWeek.AddDays(-7);

        var shifts = await repository.ListOverlappingAsync(DayStart(lastWeek), DayStart(thisWeek.AddDays(7)), userId, cancellationToken);
        var open = shifts.FirstOrDefault(s => s.IsOpen) ?? await repository.GetOpenShiftAsync(userId, cancellationToken);
        var stale = open is not null && ShiftRules.IsStale(open.ClockInUtc, now, Zone);

        return new MyTimeClockDto
        {
            OpenShift = open?.ToDto(clock, Settings),
            OpenShiftIsStale = stale,
            LeftAtEarliestLocal = stale ? clock.ToBusinessTime(open!.ClockInUtc.AddMinutes(1)) : null,
            LeftAtLatestLocal = stale ? clock.ToBusinessTime(Min(now, open!.ClockInUtc + ShiftRules.LongShift)) : null,
            RequireLocation = Settings.RequireLocation,
            KitchenLocationSet = Settings.HasKitchenLocation,
            NowLocal = clock.BusinessNow,
            ThisWeek = Workweek(thisWeek, shifts, now),
            LastWeek = Workweek(lastWeek, shifts, now),
        };
    }

    public async Task<ShiftDetailsDto?> Handle(GetShiftQuery request, CancellationToken cancellationToken)
    {
        var shift = await repository.GetAsync(request.Id, cancellationToken);
        if (shift is null)
        {
            return null;
        }

        var access = await currentStaff.GetAsync(cancellationToken);
        var own = ShiftManagementHandlers.SamePerson(shift.UserId, access.UserId);
        var canSeeTeam = access.Can(Permissions.TimeClockViewAll);
        if (!own && !canSeeTeam)
        {
            return null;
        }

        var manages = access.Can(Permissions.TimeClockManage);
        var history = (await audit.ListAsync(shift.UserId, HistorySize, cancellationToken))
            .Where(e => ShiftAudit.IsAbout(e, shift.Id))
            .Select(e => new ShiftHistoryEntryDto(ShiftAudit.ActionName(e.Action), e.Details, e.ChangedBy, clock.ToBusinessTime(e.ChangedAtUtc)))
            .ToList();

        return new ShiftDetailsDto
        {
            Shift = shift.ToDto(clock, Settings),
            Email = (await directory.GetAsync(shift.UserId, cancellationToken))?.Email,
            IsOwnShift = own,
            CanManage = manages && (!own || access.IsAdmin),
            ManageRefusal = manages && own && !access.IsAdmin ? ShiftManagementHandlers.OwnShiftRefusal : null,
            CanSeeTeam = canSeeTeam,
            KitchenLocationSet = Settings.HasKitchenLocation,
            NearKitchenRadiusMeters = Settings.NearKitchenRadiusMeters,
            History = history,
        };
    }

    public async Task<TeamTimeClockDto> Handle(GetTeamTimeClockQuery request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var thisWeek = BusinessTime.WeekStart(Today, Settings.WorkweekStartsOn);
        var open = await repository.ListOpenShiftsAsync(cancellationToken);
        var people = await directory.ListAsync(new StaffListFilter(IncludeFormer: true), cancellationToken);

        return new TeamTimeClockDto
        {
            OnTheClock = [.. open.Select(s => new OnTheClockDto(s.ToDto(clock, Settings), ShiftRules.IsStale(s.ClockInUtc, now, Zone)))],
            NeedsReviewCount = await repository.CountNeedingReviewAsync(cancellationToken),
            EditedThisWeekCount = await repository.CountEditedSinceAsync(DayStart(thisWeek), cancellationToken),
            KitchenLocationSet = Settings.HasKitchenLocation,
            NearKitchenRadiusMeters = Settings.NearKitchenRadiusMeters,
            WorkweekStartsOn = Settings.WorkweekStartsOn,
            NowLocal = clock.BusinessNow,
            Today = Today,
            ThisWeekStart = thisWeek,
            People =
            [
                .. people
                    .OrderBy(p => p.IsFormerStaff)
                    .ThenBy(p => p.FullName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(p => new TimeClockPersonDto(p.UserId, p.FullName, p.Email, p.IsFormerStaff)),
            ],
        };
    }

    public async Task<ShiftListDto> Handle(GetShiftsQuery request, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(request.Page, request.PageSize);
        var (from, to) = Ordered(request.From, request.To);
        var found = await repository.SearchAsync(Search(Person(request.UserId), from, to, request.Filter, page, pageSize), cancellationToken);
        var shifts = new PagedResult<ShiftDto>([.. found.Items.Select(s => s.ToDto(clock, Settings))], found.TotalCount, found.Page, found.PageSize);

        // Hours inside the dates: shifts crossing the first or last midnight count only their part inside.
        if (request.Filter != ShiftFilter.All || from is not { } first || to is not { } last)
        {
            return new ShiftListDto(shifts, null, null);
        }

        var (startUtc, endUtc) = (DayStart(first), DayStart(last.AddDays(1)));
        var overlapping = await repository.ListOverlappingAsync(startUtc, endUtc, Person(request.UserId), cancellationToken);
        var now = clock.UtcNow;
        return new ShiftListDto(shifts, Sum(overlapping, s => s.HoursWithin(startUtc, endUtc)), Sum(overlapping, s => s.InProgressWithin(startUtc, endUtc, now)));
    }

    public async Task<WeeklyHoursDto> Handle(GetWeeklyHoursQuery request, CancellationToken cancellationToken)
    {
        var (from, to, weeks) = await WeeklyHoursAsync(Person(request.UserId), request.From, request.To, cancellationToken);
        return new WeeklyHoursDto(Settings.WorkweekStartsOn, from, to, weeks);
    }

    public async Task<TimeClockCsvFile> Handle(ExportShiftsCsvQuery request, CancellationToken cancellationToken)
    {
        var (from, to) = Ordered(request.From, request.To);
        var found = await repository.SearchAsync(Search(Person(request.UserId), from, to, request.Filter, 1, MaxExportRows), cancellationToken);
        if (found.TotalCount > MaxExportRows)
        {
            throw new ValidationException($"That is more than {MaxExportRows:N0} shifts. Please export fewer dates at a time.");
        }

        var emails = await EmailsAsync(cancellationToken);
        var rows = found.Items
            .Select(s => s.ToDto(clock, Settings))
            .OrderBy(s => s.ClockInUtc).ThenBy(s => s.Id)
            .Select(s => ShiftRow(s, emails.GetValueOrDefault(s.UserId)));

        var range = from is not null || to is not null ? $"-{Range(from, to)}" : string.Empty;
        return new TimeClockCsvFile($"time-clock-shifts{range}.csv", TimeClockCsv.Build(ShiftColumns, rows));
    }

    public async Task<TimeClockCsvFile> Handle(ExportWeeklyHoursCsvQuery request, CancellationToken cancellationToken)
    {
        var (from, to, weeks) = await WeeklyHoursAsync(Person(request.UserId), request.From, request.To, cancellationToken);
        var rows = weeks
            .OrderBy(w => w.Start)
            .SelectMany(w => w.People.Select(p => (IReadOnlyList<string?>)
            [
                Iso(w.Start), Iso(w.End), p.Name, p.Email, Format.DecimalHours(p.Total), TimeClockText.HoursMinutes(p.Total),
                p.IsOvertime ? Format.DecimalHours(p.Overtime) : string.Empty, Number(p.ShiftCount), Number(p.FlaggedCount),
                p.InProgress > TimeSpan.Zero ? TimeClockText.HoursMinutes(p.InProgress) : string.Empty,
            ]));

        return new TimeClockCsvFile($"time-clock-weekly-hours-{Range(from, to)}.csv", TimeClockCsv.Build(WeeklyColumns, rows));
    }

    /// <summary>The workweeks containing <paramref name="from"/>…<paramref name="to"/> (at most <see cref="MaxWeeks"/>, newest first).</summary>
    private async Task<(DateOnly From, DateOnly To, IReadOnlyList<WorkweekHoursDto> Weeks)> WeeklyHoursAsync(
        string? userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var thisWeek = BusinessTime.WeekStart(Today, Settings.WorkweekStartsOn);
        var (first, last) = Ordered(from ?? thisWeek.AddDays(-7), to ?? Today);
        var lastWeek = BusinessTime.WeekStart(last!.Value, Settings.WorkweekStartsOn);
        var firstWeek = BusinessTime.WeekStart(first!.Value, Settings.WorkweekStartsOn);
        if ((lastWeek.DayNumber - firstWeek.DayNumber) / 7 >= MaxWeeks)
        {
            firstWeek = lastWeek.AddDays(-7 * (MaxWeeks - 1));
        }

        var shifts = await repository.ListOverlappingAsync(DayStart(firstWeek), DayStart(lastWeek.AddDays(7)), userId, cancellationToken);
        var emails = await EmailsAsync(cancellationToken);
        var now = clock.UtcNow;

        var weeks = new List<WorkweekHoursDto>();
        for (var start = lastWeek; start >= firstWeek; start = start.AddDays(-7))
        {
            var (startUtc, endUtc) = (DayStart(start), DayStart(start.AddDays(7)));
            var people = shifts
                .Where(s => !s.IsVoided && s.ClockInUtc < endUtc && (s.ClockOutUtc ?? now) > startUtc)
                .GroupBy(s => s.UserId, StringComparer.Ordinal)
                .Select(g => new PersonWeekHoursDto(
                    g.Key,
                    g.MaxBy(s => s.ClockInUtc)!.EmployeeName,
                    emails.GetValueOrDefault(g.Key),
                    Sum(g, s => s.HoursWithin(startUtc, endUtc)),
                    Sum(g, s => s.InProgressWithin(startUtc, endUtc, now)),
                    g.Count(),
                    g.Count(s => s.NeedsReview || s.IsMissedClockOut)))
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            weeks.Add(new WorkweekHoursDto(start, people));
        }

        return (firstWeek, lastWeek.AddDays(6), weeks);
    }

    private WorkweekShiftsDto Workweek(DateOnly start, IReadOnlyList<TimeClockShift> shifts, DateTime now)
    {
        var (startUtc, endUtc) = (DayStart(start), DayStart(start.AddDays(7)));
        var days = shifts
            .Select(s => s.ToDto(clock, Settings))
            .Where(s => s.Date >= start && s.Date < start.AddDays(7))
            .GroupBy(s => s.Date)
            .OrderBy(g => g.Key)
            .Select(g => new ShiftDayDto(g.Key, [.. g.OrderBy(s => s.ClockInUtc)]))
            .ToList();
        return new WorkweekShiftsDto(start, days, Sum(shifts, s => s.HoursWithin(startUtc, endUtc)), Sum(shifts, s => s.InProgressWithin(startUtc, endUtc, now)));
    }

    private IReadOnlyList<string?> ShiftRow(ShiftDto s, string? email)
    {
        var status = s.IsVoided ? "Voided" : s.IsOpen ? "On the clock" : s.IsMissedClockOut ? "Missed clock-out" : "Closed";
        var clockOutLocation = s.ClockOut.Status is not null ? Location(s.ClockOut) : s.IsOpen ? string.Empty : TimeClockText.ClockOutSource(s.ClockOutSource);
        return
        [
            s.EmployeeName, email, Iso(s.Date), Stamp(s.ClockInLocal), s.ClockOutLocal is { } outLocal ? Stamp(outLocal) : string.Empty,
            s.CountsTowardsHours ? Format.DecimalHours(s.Duration) : string.Empty, status, Location(s.ClockIn), clockOutLocation,
            Meters(s.ClockIn.DistanceMeters), Meters(s.ClockOut.DistanceMeters), s.ClockIn.Coordinates, s.ClockOut.Coordinates,
            s.EditedBy, s.EditReason, s.IsVoided ? $"Yes: {s.VoidReason} ({s.VoidedBy})" : string.Empty,
        ];
    }

    private static string Location(PunchDto punch) =>
        TimeClockText.Location(punch.Status) + (punch.IsApproximate ? " (approximate)" : string.Empty);

    private ShiftSearch Search(string? userId, DateOnly? from, DateOnly? to, ShiftFilter filter, int page, int pageSize) => new()
    {
        UserId = userId,
        FromUtc = from is { } first ? DayStart(first) : null,
        ToUtc = to is { } last ? DayStart(last.AddDays(1)) : null,
        Filter = filter,
        Page = page,
        PageSize = pageSize,
    };

    private async Task<Dictionary<string, string>> EmailsAsync(CancellationToken cancellationToken) =>
        (await directory.ListAsync(new StaffListFilter(IncludeFormer: true), cancellationToken))
        .GroupBy(m => m.UserId, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().Email, StringComparer.Ordinal);

    private DateTime DayStart(DateOnly date) => BusinessTime.StartOfDayUtc(date, Zone);

    /// <summary>One person's id, or null (an empty filter) for everyone.</summary>
    private static string? Person(string? userId) => string.IsNullOrWhiteSpace(userId) ? null : userId;

    private static (DateOnly? From, DateOnly? To) Ordered(DateOnly? from, DateOnly? to) =>
        from > to ? (to, from) : (from, to);

    private static TimeSpan Sum(IEnumerable<TimeClockShift> shifts, Func<TimeClockShift, TimeSpan> hours) =>
        shifts.Aggregate(TimeSpan.Zero, (total, s) => total + hours(s));

    private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Stamp(DateTime local) => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Meters(double? meters) => meters?.ToString("0", CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Range(DateOnly? from, DateOnly? to) =>
        $"{(from is { } f ? Iso(f) : "start")}-to-{(to is { } t ? Iso(t) : "now")}";
}
