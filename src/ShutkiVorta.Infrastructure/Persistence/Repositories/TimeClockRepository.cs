using System.Data.Common;
using Dapper;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;

namespace ShutkiVorta.Infrastructure.Persistence.Repositories;

/// <summary>
/// Shifts. Each change is a single statement guarded by the shift's state (still open, same version, no overlap), so racing
/// requests are told that nothing changed instead of overwriting each other. Adds and corrections, which must not overlap the
/// person's other shifts, also lock the person first.
/// </summary>
internal sealed class TimeClockRepository(IDbConnectionFactory connections, ISqlDialect dialect) : ITimeClockRepository
{
    private const string Columns = """
        Id, UserId, EmployeeName, ClockInUtc, ClockInLatitude, ClockInLongitude, ClockInAccuracyMeters, ClockInDistanceMeters,
        ClockInLocationStatus, ClockInNote, ClockOutUtc, ClockOutLatitude, ClockOutLongitude, ClockOutAccuracyMeters,
        ClockOutDistanceMeters, ClockOutLocationStatus, ClockOutNote, ClockOutSource, IsManualEntry, NeedsReview,
        OriginalClockInUtc, OriginalClockOutUtc, EditedBy, EditedAtUtc, EditReason, VoidedBy, VoidedAtUtc, VoidReason,
        Version, CreatedAtUtc, UpdatedAtUtc
        """;

    private const string Insert = """
        INSERT INTO TimeClockShifts (
            UserId, EmployeeName, ClockInUtc, ClockInLatitude, ClockInLongitude, ClockInAccuracyMeters, ClockInDistanceMeters,
            ClockInLocationStatus, ClockInNote, ClockOutUtc, ClockOutLocationStatus, ClockOutSource, IsManualEntry, NeedsReview,
            EditedBy, EditedAtUtc, EditReason, Version, CreatedAtUtc, UpdatedAtUtc)
        VALUES (
            @UserId, @EmployeeName, @ClockInUtc, @ClockInLatitude, @ClockInLongitude, @ClockInAccuracyMeters, @ClockInDistanceMeters,
            @ClockInLocationStatus, @ClockInNote, @ClockOutUtc, @ClockOutLocationStatus, @ClockOutSource, @IsManualEntry, @NeedsReview,
            @EditedBy, @EditedAtUtc, @EditReason, 0, @CreatedAtUtc, @UpdatedAtUtc)
        """;

    // Another shift of the same person (voided ones aside) that overlaps [@ClockInUtc, @ClockOutUtc); a null clock-out is open-ended.
    private const string OverlapCondition = """
        o.UserId = @UserId AND o.Id <> @ShiftId AND o.VoidedAtUtc IS NULL
        AND (@ClockOutUtc IS NULL OR o.ClockInUtc < @ClockOutUtc)
        AND (o.ClockOutUtc IS NULL OR o.ClockOutUtc > @ClockInUtc)
        """;

    public async Task<TimeClockShift?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TimeClockShift>(new CommandDefinition(
            $"SELECT {Columns} FROM TimeClockShifts WHERE Id = @Id", new { Id = id }, cancellationToken: cancellationToken));
    }

    public async Task<TimeClockShift?> GetOpenShiftAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TimeClockShift>(new CommandDefinition(
            $"SELECT {Columns} FROM TimeClockShifts WHERE UserId = @UserId AND ClockOutUtc IS NULL",
            new { UserId = userId },
            cancellationToken: cancellationToken));
    }

    public async Task<TimeClockShift?> GetLastClosedShiftAsync(string userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TimeClockShift>(new CommandDefinition(
            dialect.Page($"""
                SELECT {Columns} FROM TimeClockShifts
                WHERE UserId = @UserId AND ClockOutUtc IS NOT NULL AND VoidedAtUtc IS NULL
                ORDER BY ClockOutUtc DESC, Id DESC
                """),
            new { UserId = userId, Skip = 0, Take = 1 },
            cancellationToken: cancellationToken));
    }

    public async Task<bool> TryAddOpenShiftAsync(TimeClockShift shift, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        try
        {
            var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(Insert), shift, cancellationToken: cancellationToken));
            shift.AssignId(id);
            return true;
        }
        catch (Exception ex) when (DbErrors.IsUniqueViolation(ex))
        {
            return false; // IX_TimeClockShifts_OpenShift: the person is already on the clock.
        }
    }

    public async Task<bool> ClockOutAsync(ClockOutPunch punch, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE TimeClockShifts
               SET ClockOutUtc = CASE WHEN ClockInUtc > @ClockOutUtc THEN ClockInUtc ELSE @ClockOutUtc END,
                   ClockOutLatitude = @Latitude, ClockOutLongitude = @Longitude, ClockOutAccuracyMeters = @AccuracyMeters,
                   ClockOutDistanceMeters = @DistanceMeters, ClockOutLocationStatus = @Status, ClockOutNote = @Note,
                   ClockOutSource = @Source, NeedsReview = CASE WHEN @Flag = 1 THEN 1 ELSE NeedsReview END,
                   Version = Version + 1, UpdatedAtUtc = @ClockOutUtc
             WHERE Id = @ShiftId AND UserId = @UserId AND ClockOutUtc IS NULL
            """;

        var location = punch.Location;
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            punch.ShiftId,
            punch.UserId,
            punch.ClockOutUtc,
            location.Latitude,
            location.Longitude,
            location.AccuracyMeters,
            DistanceMeters = location.HasCoordinates ? punch.DistanceMeters : null,
            Status = (int)location.Status,
            Note = string.IsNullOrWhiteSpace(punch.Note) ? null : punch.Note.Trim(),
            Source = (int)ClockOutSource.Self,
            Flag = punch.NeedsReview ? 1 : 0,
        }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> CloseForgottenShiftAsync(ForgottenClockOut fix, CancellationToken cancellationToken = default)
    {
        // "I don't remember" closes the shift at its own clock-in time (no hours), copied in SQL so both values are identical.
        const string sql = """
            UPDATE TimeClockShifts
               SET ClockOutUtc = CASE WHEN @Missed = 1 OR ClockInUtc > @ClockOutUtc THEN ClockInUtc ELSE @ClockOutUtc END,
                   ClockOutSource = @Source, NeedsReview = 1, EditedBy = @EditedBy, EditedAtUtc = @NowUtc, EditReason = @Reason,
                   Version = Version + 1, UpdatedAtUtc = @NowUtc
             WHERE Id = @ShiftId AND UserId = @UserId AND ClockOutUtc IS NULL
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            fix.ShiftId,
            fix.UserId,
            fix.ClockOutUtc,
            Missed = fix.Source == ClockOutSource.Missed ? 1 : 0,
            Source = (int)fix.Source,
            fix.EditedBy,
            fix.NowUtc,
            fix.Reason,
        }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<ShiftChangeOutcome> ChangeTimesAsync(ShiftTimesChange change, CancellationToken cancellationToken = default)
    {
        // The first correction keeps the punched times (all SET expressions read the row as it was before this UPDATE).
        // Closing an open shift by hand records that nobody punched out on a device. An edited shift needs a review until
        // someone marks it reviewed.
        const string sql = $"""
            UPDATE TimeClockShifts
               SET OriginalClockInUtc = CASE WHEN OriginalClockInUtc IS NULL THEN ClockInUtc ELSE OriginalClockInUtc END,
                   OriginalClockOutUtc = CASE WHEN OriginalClockInUtc IS NULL THEN ClockOutUtc ELSE OriginalClockOutUtc END,
                   ClockInUtc = @ClockInUtc, ClockOutUtc = @ClockOutUtc, ClockOutSource = @Source,
                   ClockOutLocationStatus = CASE WHEN ClockOutUtc IS NULL AND @ClockOutUtc IS NOT NULL THEN @ManualEntry ELSE ClockOutLocationStatus END,
                   NeedsReview = 1, EditedBy = @EditedBy, EditedAtUtc = @NowUtc, EditReason = @Reason,
                   Version = Version + 1, UpdatedAtUtc = @NowUtc
             WHERE Id = @ShiftId AND Version = @ExpectedVersion AND VoidedAtUtc IS NULL
               AND NOT EXISTS (SELECT 1 FROM TimeClockShifts o WHERE {OverlapCondition})
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockPersonAsync(connection, transaction, change.UserId, cancellationToken);

        var updated = await connection.ExecuteAsync(new CommandDefinition(sql, new
        {
            change.ShiftId,
            change.UserId,
            change.ExpectedVersion,
            change.ClockInUtc,
            change.ClockOutUtc,
            Source = (int?)change.ClockOutSource,
            ManualEntry = (int)LocationStatus.ManualEntry,
            change.EditedBy,
            change.Reason,
            change.NowUtc,
        }, transaction, cancellationToken: cancellationToken));
        if (updated == 1)
        {
            await transaction.CommitAsync(cancellationToken);
            return ShiftChangeOutcome.Saved;
        }

        // Nothing changed: either the shift moved on (another edit, voided, deleted) or the new times overlap another shift.
        var current = await connection.QuerySingleOrDefaultAsync<VersionRow>(new CommandDefinition(
            "SELECT Version, CASE WHEN VoidedAtUtc IS NULL THEN 0 ELSE 1 END AS Voided FROM TimeClockShifts WHERE Id = @ShiftId",
            new { change.ShiftId },
            transaction,
            cancellationToken: cancellationToken));
        return current is null || current.Version != change.ExpectedVersion || current.Voided != 0
            ? ShiftChangeOutcome.Changed
            : ShiftChangeOutcome.Overlaps;
    }

    public async Task<ShiftChangeOutcome> AddClosedShiftAsync(TimeClockShift shift, CancellationToken cancellationToken = default)
    {
        // With the person locked, nobody else adds or corrects their shifts until this commits, so the overlap check
        // can come first and the insert is this transaction's last step (it never waits for anyone while holding the new row).
        await using var connection = await connections.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockPersonAsync(connection, transaction, shift.UserId, cancellationToken);

        var overlaps = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM TimeClockShifts o WHERE {OverlapCondition}",
            new { shift.UserId, ShiftId = 0, shift.ClockInUtc, shift.ClockOutUtc },
            transaction,
            cancellationToken: cancellationToken));
        if (overlaps > 0)
        {
            return ShiftChangeOutcome.Overlaps;
        }

        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition(dialect.InsertReturningId(Insert), shift, transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        shift.AssignId(id);
        return ShiftChangeOutcome.Saved;
    }

    public async Task<bool> VoidAsync(int id, int expectedVersion, string voidedBy, string reason, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        const string sql = """
            UPDATE TimeClockShifts
               SET VoidedBy = @VoidedBy, VoidedAtUtc = @NowUtc, VoidReason = @Reason, NeedsReview = 0,
                   Version = Version + 1, UpdatedAtUtc = @NowUtc
             WHERE Id = @Id AND Version = @ExpectedVersion AND VoidedAtUtc IS NULL AND ClockOutUtc IS NOT NULL
            """;

        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            sql, new { Id = id, ExpectedVersion = expectedVersion, VoidedBy = voidedBy, Reason = reason, NowUtc = nowUtc }, cancellationToken: cancellationToken)) == 1;
    }

    public async Task<bool> MarkReviewedAsync(int id, int expectedVersion, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE TimeClockShifts SET NeedsReview = 0, Version = Version + 1, UpdatedAtUtc = @NowUtc WHERE Id = @Id AND Version = @ExpectedVersion",
            new { Id = id, ExpectedVersion = expectedVersion, NowUtc = nowUtc },
            cancellationToken: cancellationToken)) == 1;
    }

    public async Task<TimeClockShift?> FindOverlapAsync(
        string userId, DateTime clockInUtc, DateTime? clockOutUtc, int? exceptShiftId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<TimeClockShift>(new CommandDefinition(
            dialect.Page($"SELECT {Columns} FROM TimeClockShifts o WHERE {OverlapCondition} ORDER BY o.ClockInUtc, o.Id"),
            new { UserId = userId, ShiftId = exceptShiftId ?? 0, ClockInUtc = clockInUtc, ClockOutUtc = clockOutUtc, Skip = 0, Take = 1 },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<TimeClockShift>> ListOverlappingAsync(
        DateTime startUtc, DateTime endUtc, string? userId, CancellationToken cancellationToken = default)
    {
        var person = userId is null ? string.Empty : "AND UserId = @UserId";
        await using var connection = await connections.OpenAsync(cancellationToken);
        var shifts = await connection.QueryAsync<TimeClockShift>(new CommandDefinition(
            $"""
            SELECT {Columns} FROM TimeClockShifts
            WHERE ClockInUtc < @EndUtc AND (ClockOutUtc IS NULL OR ClockOutUtc > @StartUtc) {person}
            ORDER BY ClockInUtc, Id
            """,
            new { StartUtc = startUtc, EndUtc = endUtc, UserId = userId },
            cancellationToken: cancellationToken));
        return shifts.AsList();
    }

    public async Task<PagedResult<TimeClockShift>> SearchAsync(ShiftSearch search, CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var parameters = new DynamicParameters();
        if (search.UserId is not null)
        {
            conditions.Add("UserId = @UserId");
            parameters.Add("UserId", search.UserId);
        }

        if (search.FromUtc is { } from)
        {
            conditions.Add("ClockInUtc >= @FromUtc");
            parameters.Add("FromUtc", from);
        }

        if (search.ToUtc is { } to)
        {
            conditions.Add("ClockInUtc < @ToUtc");
            parameters.Add("ToUtc", to);
        }

        conditions.Add(search.Filter switch
        {
            ShiftFilter.NeedsReview => "NeedsReview = 1",
            ShiftFilter.Edited => "EditedBy IS NOT NULL",
            ShiftFilter.Open => "ClockOutUtc IS NULL",
            ShiftFilter.Voided => "VoidedAtUtc IS NOT NULL",
            _ => "1 = 1",
        });

        var where = "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Skip", (search.Page - 1L) * search.PageSize); // a long: a page far past the end must not wrap around
        parameters.Add("Take", search.PageSize);

        await using var connection = await connections.OpenAsync(cancellationToken);
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM TimeClockShifts {where}", parameters, cancellationToken: cancellationToken));
        if (total == 0)
        {
            return PagedResult<TimeClockShift>.Empty(search.Page, search.PageSize);
        }

        var shifts = await connection.QueryAsync<TimeClockShift>(new CommandDefinition(
            dialect.Page($"SELECT {Columns} FROM TimeClockShifts {where} ORDER BY ClockInUtc DESC, Id DESC"), parameters, cancellationToken: cancellationToken));
        return new PagedResult<TimeClockShift>(shifts.AsList(), total, search.Page, search.PageSize);
    }

    public async Task<IReadOnlyList<TimeClockShift>> ListOpenShiftsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        var shifts = await connection.QueryAsync<TimeClockShift>(new CommandDefinition(
            $"SELECT {Columns} FROM TimeClockShifts WHERE ClockOutUtc IS NULL ORDER BY ClockInUtc, Id", cancellationToken: cancellationToken));
        return shifts.AsList();
    }

    public async Task<int> CountNeedingReviewAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM TimeClockShifts WHERE NeedsReview = 1", cancellationToken: cancellationToken));
    }

    public async Task<int> CountEditedSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM TimeClockShifts WHERE EditedAtUtc >= @SinceUtc", new { SinceUtc = sinceUtc }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<StaffAuditEntryDto>> ListAuditEntriesAsync(
        string userId, string detailsPrefix, int take, CancellationToken cancellationToken = default)
    {
        // Prefixes are like "Shift #12 (": no LIKE wildcards. The index on (TargetUserId, ChangedAtUtc) finds the person's entries.
        await using var connection = await connections.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AuditRow>(new CommandDefinition(
            dialect.Page("""
                SELECT Id, TargetUserId, TargetName, Action, Details, ChangedBy, ChangedAtUtc FROM StaffAuditLog
                WHERE TargetUserId = @UserId AND Details LIKE @Pattern
                ORDER BY ChangedAtUtc DESC, Id DESC
                """),
            new { UserId = userId, Pattern = detailsPrefix + "%", Skip = 0, Take = Math.Clamp(take, 1, 500) },
            cancellationToken: cancellationToken));
        return [.. rows.Select(r => r.ToDto())];
    }

    /// <summary>
    /// Adds and corrections of one person's shifts happen one at a time: a no-op update takes the person's Users row lock
    /// until the transaction ends, so the overlap check of the next one sees this one's result.
    /// </summary>
    private static Task LockPersonAsync(DbConnection connection, DbTransaction transaction, string userId, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            "UPDATE Users SET ConcurrencyStamp = ConcurrencyStamp WHERE Id = @UserId", new { UserId = userId }, transaction, cancellationToken: cancellationToken));

    private sealed class VersionRow
    {
        public int Version { get; init; }
        public int Voided { get; init; }
    }

    private sealed class AuditRow
    {
        public long Id { get; init; }
        public string? TargetUserId { get; init; }
        public string? TargetName { get; init; }
        public string Action { get; init; } = string.Empty;
        public string? Details { get; init; }
        public string ChangedBy { get; init; } = string.Empty;
        public DateTime ChangedAtUtc { get; init; }

        public StaffAuditEntryDto ToDto() => new()
        {
            Id = Id,
            TargetUserId = TargetUserId,
            TargetName = TargetName,
            Action = Action,
            Details = Details,
            ChangedBy = ChangedBy,
            ChangedAtUtc = DateTime.SpecifyKind(ChangedAtUtc, DateTimeKind.Utc),
        };
    }
}
