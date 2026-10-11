using Microsoft.Extensions.Options;
using NSubstitute;
using ShutkiVorta.Application.Common.Exceptions;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.UnitTests.TestDoubles;
using static ShutkiVorta.UnitTests.TestDoubles.TestShifts;

namespace ShutkiVorta.UnitTests.Application;

/// <summary>Clocking in and out, forgotten clock-outs and managers' fixes, with a clock in Dallas.</summary>
public sealed class TimeClockHandlerTests
{
    private const string Me = "user-me";
    private const string Colleague = "user-colleague";

    private readonly ITimeClockRepository _repository = Substitute.For<ITimeClockRepository>();
    private readonly IStaffDirectory _directory = Substitute.For<IStaffDirectory>();
    private readonly IStaffAuditLog _audit = Substitute.For<IStaffAuditLog>();
    private readonly ZonedClock _clock = new ZonedClock(DateTime.UtcNow).At(2026, 10, 6, 10, 0); // Tuesday 10:00 AM in Dallas
    private readonly TimeClockOptions _options = new() { KitchenLatitude = 32.7767, KitchenLongitude = -96.797, NearKitchenRadiusMeters = 300 };
    private readonly FakeCurrentStaff _staff = new(StaffAccessFor.Defaults(StaffRole.Employee, Me));

    public TimeClockHandlerTests()
    {
        _repository.TryAddOpenShiftAsync(Arg.Any<TimeClockShift>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<TimeClockShift>()?.AssignId(41);
            return true;
        });
        _repository.ClockOutAsync(Arg.Any<ClockOutPunch>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.CloseForgottenShiftAsync(Arg.Any<ForgottenClockOut>(), Arg.Any<CancellationToken>()).Returns(true);
        _repository.ChangeTimesAsync(Arg.Any<ShiftTimesChange>(), Arg.Any<CancellationToken>()).Returns(ShiftChangeOutcome.Saved);
        _repository.AddClosedShiftAsync(Arg.Any<TimeClockShift>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<TimeClockShift>()?.AssignId(77);
            return ShiftChangeOutcome.Saved;
        });
        _repository.VoidAsync(default, default, default!, default!, default, default).ReturnsForAnyArgs(true);
        _repository.MarkReviewedAsync(default, default, default, default).ReturnsForAnyArgs(true);
        _directory.GetAsync(Me, Arg.Any<CancellationToken>()).Returns(Member(Me, "Me Myself"));
        _directory.GetAsync(Colleague, Arg.Any<CancellationToken>()).Returns(Member(Colleague, "Rahim Uddin"));
    }

    private static StaffMemberDto Member(string id, string name) =>
        new() { UserId = id, FullName = name, Email = $"{id}@test.local", Role = StaffRole.Employee, EmailConfirmed = true, HasPassword = true };

    private TimeClockCommandHandlers Clock() =>
        new(_repository, _directory, _audit, _staff, new FakeCurrentUser { UserId = Me, Email = "me@test.local" }, _clock, Options.Create(_options));

    private ShiftManagementHandlers Manage() =>
        new(_repository, _directory, _audit, _staff, new FakeCurrentUser { UserId = Me, Email = "me@test.local" }, _clock);

    private TimeClockQueryHandlers Queries() => new(_repository, _directory, _staff, _clock, Options.Create(_options));

    private static PunchLocationInput AtTheKitchen => new(LocationStatus.Captured, 32.7768, -96.7971, 12);

    // ---- Clocking in and out (always the caller) ----

    [Fact]
    public async Task ClockingIn_RecordsTheCallerNow_WithTheDistanceToTheKitchen()
    {
        TimeClockShift? saved = null;
        await _repository.TryAddOpenShiftAsync(Arg.Do<TimeClockShift>(s => saved = s), Arg.Any<CancellationToken>());

        var result = await Clock().Handle(new ClockInCommand(AtTheKitchen, " running late "), CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal(Me, saved.UserId);
        Assert.Equal("Me Myself", saved.EmployeeName);
        Assert.Equal(_clock.UtcNow, saved.ClockInUtc);
        Assert.Equal(LocationStatus.Captured, saved.ClockInLocationStatus);
        Assert.InRange(saved.ClockInDistanceMeters!.Value, 10, 20);
        Assert.Equal("running late", saved.ClockInNote);
        Assert.False(saved.NeedsReview);
        Assert.True(result.ClockedIn);
        Assert.False(result.Repeated);
        Assert.Equal(new DateTime(2026, 10, 6, 10, 0, 0), result.AtLocal);
    }

    [Fact]
    public async Task ClockingIn_WithoutALocation_OrFarAway_IsFlaggedForReview_ButStillRecorded()
    {
        var saved = new List<TimeClockShift>();
        await _repository.TryAddOpenShiftAsync(Arg.Do<TimeClockShift>(saved.Add), Arg.Any<CancellationToken>());

        await Clock().Handle(new ClockInCommand(new PunchLocationInput(LocationStatus.PermissionDenied, 1, 2, 3)), CancellationToken.None);
        await Clock().Handle(new ClockInCommand(new PunchLocationInput(LocationStatus.Captured, 32.9, -96.797, 10)), CancellationToken.None);

        Assert.All(saved, s => Assert.True(s.NeedsReview));
        Assert.Null(saved[0].ClockInLatitude); // coordinates only count with a captured location
        Assert.InRange(saved[1].ClockInDistanceMeters!.Value, 13_000, 14_000);
    }

    [Fact]
    public async Task WithoutTheKitchenLocation_NoDistanceIsRecorded()
    {
        _options.KitchenLatitude = null;
        TimeClockShift? saved = null;
        await _repository.TryAddOpenShiftAsync(Arg.Do<TimeClockShift>(s => saved = s), Arg.Any<CancellationToken>());

        await Clock().Handle(new ClockInCommand(AtTheKitchen), CancellationToken.None);

        Assert.Null(saved!.ClockInDistanceMeters);
        Assert.False(saved.NeedsReview);
    }

    [Fact]
    public async Task WhenLocationIsRequired_ClockingInWithoutOne_IsRefusedWithTheFix()
    {
        _options.RequireLocation = true;

        var denied = await Assert.ThrowsAsync<ValidationException>(() =>
            Clock().Handle(new ClockInCommand(new PunchLocationInput(LocationStatus.PermissionDenied)), CancellationToken.None));
        Assert.Contains("Your location is needed to clock in", denied.Message);
        Assert.Contains("How to turn location on", denied.Message);

        var insecure = await Assert.ThrowsAsync<ValidationException>(() =>
            Clock().Handle(new ClockInCommand(new PunchLocationInput(LocationStatus.InsecureConnection)), CancellationToken.None));
        Assert.Contains("https", insecure.Message);
        await _repository.DidNotReceiveWithAnyArgs().TryAddOpenShiftAsync(default!, default);

        await Clock().Handle(new ClockInCommand(AtTheKitchen), CancellationToken.None);
        await _repository.ReceivedWithAnyArgs(1).TryAddOpenShiftAsync(default!, default);
    }

    [Fact]
    public async Task ASecondTapWithinTwoMinutes_ShowsTheFirstClockIn_LaterItIsAFriendlyError()
    {
        var open = Punched(5, Me, _clock.UtcNow.AddSeconds(-90));
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(open);

        var repeated = await Clock().Handle(new ClockInCommand(AtTheKitchen), CancellationToken.None);
        Assert.True(repeated.Repeated);
        Assert.Equal(_clock.ToBusinessTime(open.ClockInUtc), repeated.AtLocal);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(5);
        var error = await Assert.ThrowsAsync<ValidationException>(() => Clock().Handle(new ClockInCommand(AtTheKitchen), CancellationToken.None));
        Assert.Equal("You're already clocked in since 9:58 AM.", error.Message);
        await _repository.DidNotReceiveWithAnyArgs().TryAddOpenShiftAsync(default!, default);
    }

    [Fact]
    public async Task TwoTapsRacing_TheUniqueIndexRefusesTheSecond_AndItShowsTheFirst()
    {
        var first = Punched(5, Me, _clock.UtcNow);
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns((TimeClockShift?)null, first);
        _repository.TryAddOpenShiftAsync(Arg.Any<TimeClockShift>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Clock().Handle(new ClockInCommand(AtTheKitchen), CancellationToken.None);

        Assert.True(result.Repeated);
        Assert.True(result.ClockedIn);
    }

    [Fact]
    public async Task ClockingOut_ClosesTheCallersOpenShift_AndFlagsAPunchFarFromTheKitchen()
    {
        var open = Punched(5, Me, _clock.UtcNow.AddHours(-8));
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(open);
        ClockOutPunch? punch = null;
        await _repository.ClockOutAsync(Arg.Do<ClockOutPunch>(p => punch = p), Arg.Any<CancellationToken>());

        var result = await Clock().Handle(new ClockOutCommand(new PunchLocationInput(LocationStatus.Captured, 33.2, -96.797, 30)), CancellationToken.None);

        Assert.NotNull(punch);
        Assert.Equal((5, Me), (punch.ShiftId, punch.UserId));
        Assert.True(punch.NeedsReview);
        Assert.Equal(TimeSpan.FromHours(8), result.ShiftLength);
        Assert.False(result.ClockedIn);
    }

    [Fact]
    public async Task ClockingOutTwice_ShowsTheFirstWithinTwoMinutes_ThenSaysYouAreNotClockedIn()
    {
        var closed = Punched(5, Me, _clock.UtcNow.AddHours(-8), _clock.UtcNow.AddMinutes(-1));
        _repository.GetLastClosedShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(closed);

        var repeated = await Clock().Handle(new ClockOutCommand(AtTheKitchen), CancellationToken.None);
        Assert.True(repeated.Repeated);

        _clock.UtcNow = _clock.UtcNow.AddMinutes(10);
        var error = await Assert.ThrowsAsync<ValidationException>(() => Clock().Handle(new ClockOutCommand(AtTheKitchen), CancellationToken.None));
        Assert.StartsWith("You're not clocked in.", error.Message);
        await _repository.DidNotReceiveWithAnyArgs().ClockOutAsync(default!, default);
    }

    [Fact]
    public async Task AStaleShift_CannotSimplyBeClockedOut()
    {
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0)));

        var error = await Assert.ThrowsAsync<ValidationException>(() => Clock().Handle(new ClockOutCommand(AtTheKitchen), CancellationToken.None));

        Assert.Equal("You didn't clock out on Mon at 9:00 AM. Please tell us when you left first.", error.Message);
        await _repository.DidNotReceiveWithAnyArgs().ClockOutAsync(default!, default);
    }

    // ---- Forgotten clock-outs (the caller's own open shift only) ----

    [Fact]
    public async Task AForgottenShift_IsClosedWhenTheyLeft_FlaggedAndAudited()
    {
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0)));
        ForgottenClockOut? fix = null;
        await _repository.CloseForgottenShiftAsync(Arg.Do<ForgottenClockOut>(f => fix = f), Arg.Any<CancellationToken>());

        var result = await Clock().Handle(new FixMyForgottenClockOutCommand(new DateTime(2026, 10, 5, 17, 30, 0)), CancellationToken.None);

        Assert.NotNull(fix);
        Assert.Equal((5, Me, ClockOutSource.SelfCorrected), (fix.ShiftId, fix.UserId, fix.Source));
        Assert.Equal(Utc(2026, 10, 5, 17, 30), fix.ClockOutUtc);
        Assert.Equal(TimeSpan.FromHours(8.5), result.ShiftLength);
        Assert.True(result.FlaggedForReview);
        await _audit.Received(1).WriteAsync(
            Arg.Is<StaffAuditEntry>(e => e.Action == StaffAuditActions.ShiftSelfCorrected && e.TargetUserId == Me && e.Details!.StartsWith("Shift #5 (Mon, Oct 5): ")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IDontRemember_ClosesTheShiftAsMissed_WithNoHours()
    {
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0)));
        ForgottenClockOut? fix = null;
        await _repository.CloseForgottenShiftAsync(Arg.Do<ForgottenClockOut>(f => fix = f), Arg.Any<CancellationToken>());

        var result = await Clock().Handle(new FixMyForgottenClockOutCommand(null), CancellationToken.None);

        Assert.Equal(ClockOutSource.Missed, fix!.Source);
        Assert.Null(result.ShiftLength);
        await _audit.ReceivedWithAnyArgs(1).WriteAsync(default!, default);
    }

    [Theory]
    [InlineData(2026, 10, 5, 8, 0, "after you clocked in")]
    [InlineData(2026, 10, 6, 10, 30, "future")]
    [InlineData(2026, 10, 6, 2, 0, "more than 16 hours")]
    public async Task TheTimeTheyLeft_MustBePossible(int year, int month, int day, int hour, int minute, string message)
    {
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0)));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Clock().Handle(new FixMyForgottenClockOutCommand(new DateTime(year, month, day, hour, minute, 0)), CancellationToken.None));

        Assert.Contains(message, error.Message);
        Assert.Contains(nameof(FixMyForgottenClockOutCommand.LeftAtLocal), error.Errors.Keys);
        await _repository.DidNotReceiveWithAnyArgs().CloseForgottenShiftAsync(default!, default);
    }

    [Fact]
    public async Task OnlyAStaleShift_CanBeFixedThisWay()
    {
        _repository.GetOpenShiftAsync(Me, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, _clock.UtcNow.AddHours(-2)));

        var error = await Assert.ThrowsAsync<ValidationException>(() =>
            Clock().Handle(new FixMyForgottenClockOutCommand(_clock.BusinessNow.AddMinutes(-30)), CancellationToken.None));

        Assert.Equal("Your shift started today, so just clock out.", error.Message);
    }

    // ---- Managers: never their own shifts unless they are an Admin ----

    [Fact]
    public async Task AManager_CannotCorrectTheirOwnShift_HoweverTheIdIsWritten()
    {
        _staff.Access = StaffAccessFor.Custom(StaffRole.Manager, [Permissions.TimeClockManage], Me);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 0)));
        _directory.GetAsync("USER-ME", Arg.Any<CancellationToken>()).Returns(Member(Me, "Me Myself"));

        var correct = new CorrectShiftCommand(5, 0, new DateTime(2026, 10, 5, 7, 0, 0), new DateTime(2026, 10, 5, 18, 0, 0), "forgot");
        Assert.Equal("Ask an admin to fix your own shifts.", (await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(correct, CancellationToken.None))).Message);
        await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(new VoidShiftCommand(5, 0, "mistake"), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(new MarkShiftReviewedCommand(5, 0), CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() =>
            Manage().Handle(new AddShiftCommand(" USER-ME ", new DateTime(2026, 10, 4, 9, 0, 0), new DateTime(2026, 10, 4, 17, 0, 0), "extra"), CancellationToken.None));

        await _repository.DidNotReceiveWithAnyArgs().ChangeTimesAsync(default!, default);
        await _repository.DidNotReceiveWithAnyArgs().AddClosedShiftAsync(default!, default);
        await _audit.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Fact]
    public async Task AManager_CorrectsSomeoneElsesShift_AndItIsAudited()
    {
        _staff.Access = StaffAccessFor.Custom(StaffRole.Manager, [Permissions.TimeClockManage], Me);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Colleague, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 0)));
        ShiftTimesChange? change = null;
        await _repository.ChangeTimesAsync(Arg.Do<ShiftTimesChange>(c => change = c), Arg.Any<CancellationToken>());

        await Manage().Handle(new CorrectShiftCommand(5, 3, new DateTime(2026, 10, 5, 8, 30, 0), new DateTime(2026, 10, 5, 17, 0, 0), " forgot to clock in "), CancellationToken.None);

        Assert.NotNull(change);
        Assert.Equal((5, Colleague, 3), (change.ShiftId, change.UserId, change.ExpectedVersion));
        Assert.Equal(Utc(2026, 10, 5, 8, 30), change.ClockInUtc);
        Assert.Equal(ClockOutSource.Self, change.ClockOutSource); // the clock-out did not change
        Assert.Equal("forgot to clock in", change.Reason);
        await _audit.Received(1).WriteAsync(Arg.Is<StaffAuditEntry>(e =>
            e.Action == StaffAuditActions.ShiftCorrected && e.TargetUserId == Colleague && e.ChangedBy == "me@test.local"
            && e.Details == "Shift #5 (Mon, Oct 5): Mon, Oct 5, 9:00 AM – 5:00 PM → Mon, Oct 5, 8:30 AM – 5:00 PM. Reason: forgot to clock in"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CorrectingAMissedClockOut_MakesItCount_EvenWhenTheClockOutStaysAtThePlaceholder()
    {
        _staff.Access = StaffAccessFor.Custom(StaffRole.Manager, [Permissions.TimeClockManage], Me);
        var missed = Punched(5, Colleague, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 9, 0)).With(nameof(TimeClockShift.ClockOutSource), ClockOutSource.Missed);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(missed);
        ShiftTimesChange? change = null;
        await _repository.ChangeTimesAsync(Arg.Do<ShiftTimesChange>(c => change = c), Arg.Any<CancellationToken>());

        // They worked 7:00 to 9:00: the clock-out typed is the placeholder (the old clock-in).
        await Manage().Handle(new CorrectShiftCommand(5, 2, new DateTime(2026, 10, 5, 7, 0, 0), new DateTime(2026, 10, 5, 9, 0, 0), "asked them"), CancellationToken.None);

        Assert.Equal((Utc(2026, 10, 5, 7, 0), Utc(2026, 10, 5, 9, 0)), (change!.ClockInUtc, change.ClockOutUtc));
        Assert.Equal(ClockOutSource.Manager, change.ClockOutSource);
    }

    [Fact]
    public async Task AnAdmin_MayFixTheirOwnShifts()
    {
        _staff.Access = StaffAccessFor.Admin(Me);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Me, Utc(2026, 10, 5, 9, 0)));

        await Manage().Handle(new CloseShiftCommand(5, 0, new DateTime(2026, 10, 5, 17, 0, 0), "forgot"), CancellationToken.None);

        await _repository.Received(1).ChangeTimesAsync(Arg.Is<ShiftTimesChange>(c => c.ClockOutSource == ClockOutSource.Manager), Arg.Any<CancellationToken>());
        await _audit.Received(1).WriteAsync(Arg.Is<StaffAuditEntry>(e => e.Action == StaffAuditActions.ShiftClosed), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Corrections_ThatOverlapAnotherShift_OrRaceAnotherChange_SayWhy()
    {
        _staff.Access = StaffAccessFor.Admin(Me);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Colleague, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 0)));
        _repository.ChangeTimesAsync(Arg.Any<ShiftTimesChange>(), Arg.Any<CancellationToken>()).Returns(ShiftChangeOutcome.Overlaps, ShiftChangeOutcome.Changed);
        _repository.FindOverlapAsync(Colleague, Arg.Any<DateTime>(), Arg.Any<DateTime?>(), 5, Arg.Any<CancellationToken>())
            .Returns(Punched(6, Colleague, Utc(2026, 10, 5, 18, 0), Utc(2026, 10, 5, 22, 0)));
        var command = new CorrectShiftCommand(5, 0, new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 19, 0, 0), "stayed late");

        var overlap = await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(command, CancellationToken.None));
        Assert.Contains("overlap another shift of this person (Mon, Oct 5, 6:00 PM – 10:00 PM)", overlap.Message);
        var changed = await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(command, CancellationToken.None));
        Assert.StartsWith("Someone changed this shift", changed.Message);
        await _audit.DidNotReceiveWithAnyArgs().WriteAsync(default!, default);
    }

    [Fact]
    public async Task ManagersEnterBusinessTimes_ThatExist_AndNotInTheFuture()
    {
        _staff.Access = StaffAccessFor.Admin(Me);
        var skipped = await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(
            new AddShiftCommand(Colleague, new DateTime(2026, 3, 8, 2, 30, 0), new DateTime(2026, 3, 8, 9, 0, 0), "night prep"), CancellationToken.None));
        Assert.Equal("That time doesn't exist (clocks moved forward).", skipped.Message);
        Assert.Contains(nameof(AddShiftCommand.ClockInLocal), skipped.Errors.Keys);

        var future = await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(
            new AddShiftCommand(Colleague, new DateTime(2026, 10, 6, 9, 0, 0), new DateTime(2026, 10, 6, 17, 0, 0), "tomorrow"), CancellationToken.None));
        Assert.Contains("future", future.Message);

        var unknown = await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(
            new AddShiftCommand("nobody", new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 17, 0, 0), "who?"), CancellationToken.None));
        Assert.Contains(nameof(AddShiftCommand.UserId), unknown.Errors.Keys);

        var id = await Manage().Handle(
            new AddShiftCommand(Colleague, new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 17, 0, 0), "phone died"), CancellationToken.None);
        Assert.Equal(77, id);
        await _audit.Received(1).WriteAsync(Arg.Is<StaffAuditEntry>(e => e.Action == StaffAuditActions.ShiftAdded && e.TargetName == "Rahim Uddin"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenShiftsCannotBeVoided_AndVoidingIsAudited()
    {
        _staff.Access = StaffAccessFor.Admin(Me);
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Colleague, Utc(2026, 10, 5, 9, 0)));
        _repository.GetAsync(6, Arg.Any<CancellationToken>()).Returns(Punched(6, Colleague, Utc(2026, 10, 4, 9, 0), Utc(2026, 10, 4, 12, 0)));

        await Assert.ThrowsAsync<ValidationException>(() => Manage().Handle(new VoidShiftCommand(5, 0, "mistake"), CancellationToken.None));
        await Manage().Handle(new VoidShiftCommand(6, 0, "clocked in by mistake"), CancellationToken.None);
        await Assert.ThrowsAsync<NotFoundException>(() => Manage().Handle(new VoidShiftCommand(99, 0, "gone"), CancellationToken.None));

        await _audit.Received(1).WriteAsync(Arg.Is<StaffAuditEntry>(e => e.Action == StaffAuditActions.ShiftVoided && e.Details!.Contains("clocked in by mistake")), Arg.Any<CancellationToken>());
    }

    // ---- Seeing shifts ----

    [Fact]
    public async Task AShiftPage_IsOnlyForItsOwner_OrPeopleWhoSeeTheTeam()
    {
        _repository.GetAsync(5, Arg.Any<CancellationToken>()).Returns(Punched(5, Colleague, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 0)));
        _repository.GetAsync(6, Arg.Any<CancellationToken>()).Returns(Punched(6, Me, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 0)));
        _repository.ListAuditEntriesAsync(default!, default!, default, default).ReturnsForAnyArgs([]);
        _repository.ListAuditEntriesAsync(Me, "Shift #6 (", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
        [
            new StaffAuditEntryDto { Id = 9, TargetUserId = Me, Action = StaffAuditActions.ShiftCorrected, Details = "Shift #6 (Mon, Oct 5): earlier", ChangedBy = "boss@test.local" },
        ]);

        Assert.Null(await Queries().Handle(new GetShiftQuery(5), CancellationToken.None));
        Assert.Null(await Queries().Handle(new GetShiftQuery(404), CancellationToken.None));
        var own = await Queries().Handle(new GetShiftQuery(6), CancellationToken.None);
        Assert.NotNull(own);
        Assert.True(own.IsOwnShift);
        Assert.False(own.CanManage);
        Assert.Null(own.ManageRefusal);
        Assert.Equal(("Shift corrected", "boss@test.local"), (Assert.Single(own.History).Action, own.History[0].ChangedBy)); // its own entries, found by the database

        _staff.Access = StaffAccessFor.Custom(StaffRole.Manager, [Permissions.TimeClockManage], Me);
        var others = await Queries().Handle(new GetShiftQuery(5), CancellationToken.None);
        Assert.True(others!.CanManage);
        var mine = await Queries().Handle(new GetShiftQuery(6), CancellationToken.None);
        Assert.False(mine!.CanManage);
        Assert.Equal("Ask an admin to fix your own shifts.", mine.ManageRefusal);

        _staff.Access = StaffAccessFor.Admin(Me);
        Assert.True((await Queries().Handle(new GetShiftQuery(6), CancellationToken.None))!.CanManage);
    }

    [Fact]
    public async Task MyTimeClock_ShowsAStaleShift_AndThisAndLastWeeksHours()
    {
        var shifts = new List<TimeClockShift>
        {
            Punched(1, Me, Utc(2026, 9, 29, 9, 0), Utc(2026, 9, 29, 17, 30)), // last week (Tue)
            Punched(2, Me, Utc(2026, 10, 4, 22, 0), Utc(2026, 10, 5, 4, 0)), // Sun night into this week
            Punched(3, Me, Utc(2026, 10, 5, 9, 0)), // forgotten: still open on Tuesday
        };
        _repository.ListOverlappingAsync(default, default, default, default).ReturnsForAnyArgs(shifts);

        var mine = await Queries().Handle(new GetMyTimeClockQuery(), CancellationToken.None);

        Assert.Equal(3, mine.OpenShift!.Id);
        Assert.True(mine.OpenShiftIsStale);
        Assert.Equal(new DateTime(2026, 10, 5, 9, 1, 0), mine.LeftAtEarliestLocal);
        Assert.Equal(new DateTime(2026, 10, 6, 1, 0, 0), mine.LeftAtLatestLocal);
        Assert.Equal(new DateOnly(2026, 10, 5), mine.ThisWeek.Start);
        Assert.Equal(TimeSpan.FromHours(4), mine.ThisWeek.Total);
        Assert.Equal(TimeSpan.FromHours(10.5), mine.LastWeek.Total);
        Assert.Equal(TimeSpan.FromHours(25), mine.ThisWeek.InProgress); // shown as "so far", not counted
        Assert.Equal([new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 4)], mine.LastWeek.Days.Select(d => d.Date));
    }

    [Fact]
    public async Task WeeklyHours_LeaveOutVoidedAndMissedShifts_AndHighlightOvertime()
    {
        var shifts = new List<TimeClockShift>();
        for (var day = 5; day <= 9; day++)
        {
            shifts.Add(Punched(day, Colleague, Utc(2026, 10, day, 8, 0), Utc(2026, 10, day, 17, 0))); // 5 × 9 h = 45 h
        }

        shifts.Add(Punched(20, Colleague, Utc(2026, 10, 10, 8, 0), Utc(2026, 10, 10, 18, 0)).With(nameof(TimeClockShift.VoidedAtUtc), Utc(2026, 10, 10, 19, 0)));
        shifts.Add(Punched(21, Colleague, Utc(2026, 10, 4, 9, 0), Utc(2026, 10, 4, 9, 0)).With(nameof(TimeClockShift.ClockOutSource), ClockOutSource.Missed)
            .With(nameof(TimeClockShift.NeedsReview), true));
        _repository.ListOverlappingAsync(default, default, default, default).ReturnsForAnyArgs(shifts);
        _directory.ListAsync(default!, default).ReturnsForAnyArgs([Member(Colleague, "Rahim Uddin")]);
        _staff.Access = StaffAccessFor.Admin(Me);
        _clock.At(2026, 10, 11, 12, 0);

        var weekly = await Queries().Handle(new GetWeeklyHoursQuery(), CancellationToken.None);

        Assert.Equal([new DateOnly(2026, 10, 5), new DateOnly(2026, 9, 28)], weekly.Weeks.Select(w => w.Start));
        var thisWeek = Assert.Single(weekly.Weeks[0].People);
        Assert.Equal(TimeSpan.FromHours(45), thisWeek.Total);
        Assert.True(thisWeek.IsOvertime);
        Assert.Equal(TimeSpan.FromHours(5), thisWeek.Overtime);
        Assert.Equal(5, thisWeek.ShiftCount); // the voided one is left out
        var lastWeek = Assert.Single(weekly.Weeks[1].People);
        Assert.Equal(TimeSpan.Zero, lastWeek.Total); // a missed clock-out counts no hours until fixed
        Assert.Equal(1, lastWeek.FlaggedCount);
    }

    // ---- CSV exports ----

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")", "\"'=HYPERLINK(\"\"http://x\"\")\"")]
    [InlineData("+1 214", "'+1 214")]
    [InlineData("-96.7970", "'-96.7970")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTab", "'\tTab")]
    [InlineData("\rReturn", "\"'\rReturn\"")]
    [InlineData("Rahim, Jr.", "\"Rahim, Jr.\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void CsvCells_CannotStartAFormula_AndAreQuotedWhenNeeded(string value, string expected)
    {
        Assert.Equal(expected, TimeClockCsv.Cell(value));
    }

    [Fact]
    public async Task TheShiftExport_HasThePayrollColumns_AndNeutralisesEveryTextCell()
    {
        _staff.Access = StaffAccessFor.Admin(Me);
        var corrected = Punched(5, Colleague, Utc(2026, 10, 5, 9, 0), Utc(2026, 10, 5, 17, 15), name: "=cmd|' /C calc'!A0")
            .With(nameof(TimeClockShift.EditedBy), "manager@test.local")
            .With(nameof(TimeClockShift.EditReason), "@forgot, came early");
        _repository.SearchAsync(default!, default).ReturnsForAnyArgs(new PagedResult<TimeClockShift>([corrected], 1, 1, 20_000));
        _directory.ListAsync(default!, default).ReturnsForAnyArgs([Member(Colleague, "Rahim Uddin")]);

        var file = await Queries().Handle(new ExportShiftsCsvQuery { From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 6) }, CancellationToken.None);

        Assert.Equal("time-clock-shifts-2026-10-01-to-2026-10-06.csv", file.FileName);
        Assert.Equal([0xEF, 0xBB, 0xBF], file.Content.Take(3));
        var lines = System.Text.Encoding.UTF8.GetString(file.Content[3..]).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "Name,Email,Date,Clock in,Clock out,Hours,Status,Clock-in location,Clock-out location,Clock-in distance (m),Clock-out distance (m),"
            + "Clock-in coordinates,Clock-out coordinates,Edited by,Reason,Voided",
            lines[0]);
        Assert.Equal(
            "'=cmd|' /C calc'!A0,user-colleague@test.local,2026-10-05,2026-10-05 09:00,2026-10-05 17:15,8.25,Closed,Location recorded,Location recorded,,,"
            + "\"32.776700,-96.797000\",,manager@test.local,\"'@forgot, came early\",",
            lines[1]);
    }
}
