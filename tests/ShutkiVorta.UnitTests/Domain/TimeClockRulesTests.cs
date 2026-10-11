using ShutkiVorta.Domain.Common;
using ShutkiVorta.Domain.TimeClock;
using static ShutkiVorta.UnitTests.TestDoubles.TestShifts;

namespace ShutkiVorta.UnitTests.Domain;

/// <summary>The time clock's pure rules, in Dallas time (America/Chicago) so business days and daylight saving are real.</summary>
public sealed class TimeClockRulesTests
{
    // ---- Distance ----

    [Fact]
    public void Haversine_GivesTheGreatCircleDistanceInMetres()
    {
        Assert.Equal(0, GeoDistance.Meters(32.7767, -96.797, 32.7767, -96.797), precision: 6);

        // One degree of latitude is R·π/180.
        Assert.Equal(111_195.08, GeoDistance.Meters(0, 0, 1, 0), precision: 1);

        // Downtown Dallas to downtown Fort Worth, about 50 km; symmetric.
        var dallasToFortWorth = GeoDistance.Meters(32.7767, -96.7970, 32.7555, -97.3308);
        Assert.InRange(dallasToFortWorth, 49_500, 50_500);
        Assert.Equal(dallasToFortWorth, GeoDistance.Meters(32.7555, -97.3308, 32.7767, -96.7970), precision: 6);

        // Opposite sides of the Earth: half its circumference, without NaN from rounding.
        Assert.Equal(Math.PI * GeoDistance.EarthRadiusMeters, GeoDistance.Meters(0, 0, 0, 180), precision: 0);
    }

    // ---- Punch locations ----

    [Theory]
    [InlineData(double.NaN, -96.8, 10.0)]
    [InlineData(32.7, double.PositiveInfinity, 10.0)]
    [InlineData(90.5, -96.8, 10.0)]
    [InlineData(32.7, -180.5, 10.0)]
    [InlineData(32.7, -96.8, double.NaN)]
    [InlineData(32.7, -96.8, -1.0)]
    [InlineData(32.7, -96.8, 100_001.0)]
    public void ALocationNoDeviceCouldReport_IsRefused(double latitude, double longitude, double accuracy)
    {
        Assert.Throws<DomainException>(() => PunchLocation.Captured(latitude, longitude, accuracy));
    }

    [Fact]
    public void AccuracyWorseThanOneKilometre_IsApproximate()
    {
        Assert.False(PunchLocation.Captured(32.7, -96.8, 1000).IsApproximate);
        Assert.True(PunchLocation.Captured(32.7, -96.8, 1000.5).IsApproximate);
        Assert.False(PunchLocation.Captured(32.7, -96.8, null).IsApproximate);
        Assert.True(PunchLocation.Captured(-90, 180, 0).HasCoordinates);
    }

    [Fact]
    public void AMissingLocation_KeepsOnlyWhyItIsMissing()
    {
        var denied = PunchLocation.Missing(LocationStatus.PermissionDenied);
        Assert.False(denied.HasCoordinates);
        Assert.Null(denied.Latitude);
        Assert.Throws<DomainException>(() => PunchLocation.Missing(LocationStatus.Captured));
        Assert.Throws<DomainException>(() => PunchLocation.Missing(LocationStatus.ManualEntry));
        Assert.Throws<DomainException>(() => PunchLocation.Missing((LocationStatus)99));
    }

    [Fact]
    public void ADoubtfulPunch_HasNoLocation_OnlyARoughOne_OrIsFarFromTheKitchen()
    {
        var precise = PunchLocation.Captured(32.7767, -96.797, 15);
        Assert.False(ShiftRules.IsDoubtfulPunch(precise, 120, 300));
        Assert.False(ShiftRules.IsDoubtfulPunch(precise, null, 300)); // kitchen location not set
        Assert.True(ShiftRules.IsDoubtfulPunch(precise, 300.1, 300));
        Assert.True(ShiftRules.IsDoubtfulPunch(PunchLocation.Captured(32.7767, -96.797, 2500), 10, 300));
        Assert.True(ShiftRules.IsDoubtfulPunch(PunchLocation.Missing(LocationStatus.Timeout), null, 300));
    }

    // ---- Forgotten clock-outs ----

    [Fact]
    public void AnOpenShiftFromAnEarlierBusinessDay_IsStale_EvenIfItIsShort()
    {
        var clockIn = Utc(2026, 10, 5, 18, 0); // Mon 6:00 PM in Dallas
        Assert.False(ShiftRules.IsStale(clockIn, Utc(2026, 10, 5, 23, 59), Dallas));
        Assert.True(ShiftRules.IsStale(clockIn, Utc(2026, 10, 6, 0, 30), Dallas));
    }

    [Fact]
    public void BusinessDays_AreDallasDays_NotUtcDays()
    {
        // 6:30 PM and 7:30 PM in Dallas are different UTC days (23:30Z and 00:30Z) but the same business day.
        var clockIn = Utc(2026, 10, 5, 18, 30);
        var now = Utc(2026, 10, 5, 19, 30);
        Assert.NotEqual(clockIn.Date, now.Date);
        Assert.False(ShiftRules.IsStale(clockIn, now, Dallas));
    }

    [Fact]
    public void AnOpenShiftOverSixteenHoursOld_IsStale_OnTheSameDay()
    {
        var clockIn = Utc(2026, 10, 5, 0, 10);
        Assert.False(ShiftRules.IsStale(clockIn, Utc(2026, 10, 5, 16, 10), Dallas));
        Assert.True(ShiftRules.IsStale(clockIn, Utc(2026, 10, 5, 16, 11), Dallas));
    }

    [Fact]
    public void TheTimeSomeoneSaysTheyLeft_IsAfterClockIn_NotInTheFuture_AndAtMostSixteenHoursLater()
    {
        var clockIn = Utc(2026, 10, 5, 9, 0);
        var now = Utc(2026, 10, 6, 8, 0);
        ShiftRules.CheckForgottenClockOut(clockIn, Utc(2026, 10, 5, 17, 30), now);
        ShiftRules.CheckForgottenClockOut(clockIn, clockIn + ShiftRules.LongShift, now);

        Assert.Throws<DomainException>(() => ShiftRules.CheckForgottenClockOut(clockIn, clockIn, now));
        Assert.Throws<DomainException>(() => ShiftRules.CheckForgottenClockOut(clockIn, Utc(2026, 10, 5, 8, 0), now));
        Assert.Throws<DomainException>(() => ShiftRules.CheckForgottenClockOut(clockIn, clockIn + ShiftRules.LongShift + TimeSpan.FromMinutes(1), now));
        Assert.Throws<DomainException>(() => ShiftRules.CheckForgottenClockOut(Utc(2026, 10, 6, 7, 0), Utc(2026, 10, 6, 9, 0), now));
    }

    // ---- Corrections ----

    [Fact]
    public void CorrectedTimes_EndAfterTheyStart_AreNotInTheFutureOrBefore2000_AndLastAtMost24Hours()
    {
        var now = Utc(2026, 10, 8, 12, 0);
        var clockIn = Utc(2026, 10, 7, 9, 0);
        ShiftRules.CheckTimes(clockIn, Utc(2026, 10, 7, 17, 0), now);
        ShiftRules.CheckTimes(clockIn, clockIn.AddHours(24), now);
        ShiftRules.CheckTimes(clockIn, null, now); // stays open
        ShiftRules.CheckTimes(Utc(2000, 1, 1, 9, 0), Utc(2000, 1, 1, 17, 0), now);

        Assert.Equal("The clock-out must be after the clock-in.", Assert.Throws<DomainException>(() => ShiftRules.CheckTimes(clockIn, clockIn, now)).Message);
        Assert.Contains("at most 24 hours", Assert.Throws<DomainException>(() => ShiftRules.CheckTimes(clockIn, clockIn.AddHours(24).AddMinutes(1), now)).Message);
        Assert.Contains("future", Assert.Throws<DomainException>(() => ShiftRules.CheckTimes(clockIn, now.AddMinutes(1), now)).Message);
        Assert.Contains("future", Assert.Throws<DomainException>(() => ShiftRules.CheckTimes(now.AddMinutes(1), null, now)).Message);

        // A year typed as 0202 (the date picker allows it).
        Assert.Equal(
            "Please check the year: a shift can't start before 2000.",
            Assert.Throws<DomainException>(() => ShiftRules.CheckTimes(Utc(202, 10, 7, 9, 0), Utc(202, 10, 7, 17, 0), now)).Message);
    }

    [Fact]
    public void ATimeSkippedWhenClocksMoveForward_IsRefused()
    {
        // Dallas: Sunday March 14, 2027, 2:00 AM became 3:00 AM.
        var exception = Assert.Throws<DomainException>(() => BusinessTime.FromLocalInput(new DateTime(2027, 3, 14, 2, 30, 0), Dallas));
        Assert.Equal("That time doesn't exist (clocks moved forward).", exception.Message);
        Assert.Throws<DomainException>(() => BusinessTime.FromLocalInput(new DateTime(2026, 3, 8, 2, 0, 0), Dallas));

        Assert.Equal(new DateTime(2027, 3, 14, 7, 59, 0), BusinessTime.FromLocalInput(new DateTime(2027, 3, 14, 1, 59, 0), Dallas));
        Assert.Equal(new DateTime(2027, 3, 14, 8, 0, 0), BusinessTime.FromLocalInput(new DateTime(2027, 3, 14, 3, 0, 0), Dallas));
    }

    [Fact]
    public void ATimeThatHappenedTwiceWhenClocksMovedBack_MeansTheFirstOne()
    {
        // Dallas: Sunday November 1, 2026, 2:00 AM daylight time became 1:00 AM standard time.
        var utc = BusinessTime.FromLocalInput(new DateTime(2026, 11, 1, 1, 30, 0), Dallas);
        Assert.Equal(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    // ---- Hours in a period ----

    [Fact]
    public void ANightShiftAcrossTheStartOfTheWeek_CountsInBothWeeks_EachItsOwnPart()
    {
        // Sun 10:00 PM → Mon 4:00 AM; the workweek starts on Monday.
        var clockIn = Utc(2026, 10, 4, 22, 0);
        var clockOut = Utc(2026, 10, 5, 4, 0);
        var monday = BusinessTime.StartOfDayUtc(new DateOnly(2026, 10, 5), Dallas);
        var lastWeek = BusinessTime.StartOfDayUtc(new DateOnly(2026, 9, 28), Dallas);
        var nextWeek = BusinessTime.StartOfDayUtc(new DateOnly(2026, 10, 12), Dallas);

        Assert.Equal(TimeSpan.FromHours(2), ShiftRules.Clip(clockIn, clockOut, clockOut, lastWeek, monday));
        Assert.Equal(TimeSpan.FromHours(4), ShiftRules.Clip(clockIn, clockOut, clockOut, monday, nextWeek));
        Assert.Equal(TimeSpan.Zero, ShiftRules.Clip(clockIn, clockOut, clockOut, nextWeek, nextWeek.AddDays(7)));
    }

    [Fact]
    public void AnOpenShift_CountsUntilNow_AndNothingOutsideThePeriod()
    {
        var start = Utc(2026, 10, 5, 0, 0);
        var end = Utc(2026, 10, 6, 0, 0);
        Assert.Equal(TimeSpan.FromHours(3), ShiftRules.Clip(Utc(2026, 10, 5, 9, 0), null, Utc(2026, 10, 5, 12, 0), start, end));
        Assert.Equal(TimeSpan.Zero, ShiftRules.Clip(Utc(2026, 10, 7, 9, 0), null, Utc(2026, 10, 7, 12, 0), start, end));
    }

    [Fact]
    public void HoursAcrossTheFallBackNight_AreRealHours()
    {
        // Sat 10:00 PM → Sun 6:00 AM on the night clocks move back: 8 hours on the wall clock, 9 worked.
        var clockIn = BusinessTime.FromLocalInput(new DateTime(2026, 10, 31, 22, 0, 0), Dallas);
        var clockOut = BusinessTime.FromLocalInput(new DateTime(2026, 11, 1, 6, 0, 0), Dallas);
        Assert.Equal(TimeSpan.FromHours(9), ShiftRules.Clip(clockIn, clockOut, clockOut, DateTime.MinValue, DateTime.MaxValue));

        // That Sunday has 25 hours, the spring-forward Sunday 23.
        var sunday = BusinessTime.StartOfDayUtc(new DateOnly(2026, 11, 1), Dallas);
        Assert.Equal(TimeSpan.FromHours(25), BusinessTime.StartOfDayUtc(new DateOnly(2026, 11, 2), Dallas) - sunday);
        Assert.Equal(TimeSpan.FromHours(23),
            BusinessTime.StartOfDayUtc(new DateOnly(2027, 3, 15), Dallas) - BusinessTime.StartOfDayUtc(new DateOnly(2027, 3, 14), Dallas));
        Assert.Equal(TimeSpan.FromHours(7), ShiftRules.Clip(clockIn, clockOut, clockOut, sunday, sunday.AddDays(1)));
    }

    [Fact]
    public void ADayWhoseMidnightIsSkipped_StartsWhenTheClocksJump()
    {
        // A zone (like Cuba's) that moves its clocks forward at midnight: March 8, 2026 starts at 1:00 AM.
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test/MidnightJump", TimeSpan.FromHours(-5), "Midnight jump", "Standard", "Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 11, 1, DayOfWeek.Sunday)),
        ]);
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 8, 0, 0, 0)));

        var start = BusinessTime.StartOfDayUtc(new DateOnly(2026, 3, 8), zone);
        Assert.Equal(new DateTime(2026, 3, 8, 5, 0, 0), start);
        Assert.Equal(new DateTime(2026, 3, 8, 1, 0, 0), TimeZoneInfo.ConvertTimeFromUtc(start, zone));
    }

    [Theory]
    [InlineData("2026-10-11", DayOfWeek.Monday, "2026-10-05")] // a Sunday
    [InlineData("2026-10-05", DayOfWeek.Monday, "2026-10-05")]
    [InlineData("2026-10-11", DayOfWeek.Sunday, "2026-10-11")]
    [InlineData("2026-10-10", DayOfWeek.Sunday, "2026-10-04")]
    [InlineData("2026-10-08", DayOfWeek.Friday, "2026-10-02")]
    public void TheWorkweek_StartsOnTheConfiguredDay(string date, DayOfWeek firstDay, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), BusinessTime.WeekStart(DateOnly.Parse(date), firstDay));
    }

    // ---- Shifts ----

    [Fact]
    public void OpenShifts_AndShiftsAddedByHand_CountTheRightHours()
    {
        var clockIn = Utc(2026, 10, 5, 9, 0);
        var open = TimeClockShift.Open("u1", "  Rahim  ", PunchLocation.Missing(LocationStatus.PermissionDenied), 50, " ", true, clockIn);
        Assert.True(open.IsOpen);
        Assert.False(open.CountsTowardsHours);
        Assert.Equal("Rahim", open.EmployeeName);
        Assert.Null(open.ClockInNote);
        Assert.Null(open.ClockInDistanceMeters); // no location, no distance
        Assert.Equal(TimeSpan.FromHours(2), open.Duration(clockIn.AddHours(2)));
        Assert.Equal(TimeSpan.Zero, open.HoursWithin(DateTime.MinValue, DateTime.MaxValue));
        Assert.Equal(TimeSpan.FromHours(2), open.InProgressWithin(DateTime.MinValue, DateTime.MaxValue, clockIn.AddHours(2)));

        var added = TimeClockShift.AddByHand("u1", "Rahim", clockIn, clockIn.AddHours(8), "manager@test.local", "Phone battery died", clockIn.AddDays(1));
        Assert.True(added.IsManualEntry);
        Assert.Equal(LocationStatus.ManualEntry, added.ClockInLocationStatus);
        Assert.Equal(ClockOutSource.Manager, added.ClockOutSource);
        Assert.Equal(TimeSpan.FromHours(8), added.HoursWithin(DateTime.MinValue, DateTime.MaxValue));
        Assert.Throws<DomainException>(() => TimeClockShift.AddByHand("u1", "Rahim", clockIn, clockIn.AddHours(8), "m", " ", clockIn.AddDays(1)));
        Assert.Throws<DomainException>(() => TimeClockShift.Open("u1", "Rahim", PunchLocation.ManualEntry, null, null, false, clockIn));
    }
}
