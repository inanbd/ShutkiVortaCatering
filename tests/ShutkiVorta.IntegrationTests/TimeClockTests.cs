using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Staff;
using ShutkiVorta.Application.Features.TimeClock;
using ShutkiVorta.Domain.TimeClock;
using ShutkiVorta.Infrastructure.Persistence;
using ShutkiVorta.IntegrationTests.Infrastructure;
using ShutkiVorta.Web.Infrastructure.Security;
using static ShutkiVorta.IntegrationTests.Infrastructure.StaffTestHelper;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// The time clock end to end: clocking in and out with a location, forgotten clock-outs, who may see and fix which shifts,
/// hours and exports. Tests in the shared app use their own people; tests that change the Time clock settings use apps of their own.
/// </summary>
[Collection("app")]
public sealed partial class TimeClockTests(TestServers servers)
{
    private const string Page = "/admin/time-clock";

    // ---- Clocking in and out ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AnEmployee_ClocksInAndOut_WithTheirLocation_AndRepeatedTapsAreFriendly(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);

        var page = await client.GetAsync(Page);
        Assert.Equal("camera=(), microphone=(), geolocation=(self)", string.Join(",", page.Headers.GetValues("Permissions-Policy")));
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("You're not clocked in", html);
        Assert.Contains("Clock in</span> <span aria-hidden=\"true\">·</span> <span class=\"bn\" lang=\"bn\">কাজ শুরু", html);
        Assert.Contains("Location is reported by your phone or computer.", html);
        Assert.Contains("Your location is saved only when you clock in or out.", html);
        Assert.Contains("How to turn location on", html);
        Assert.Contains("action=\"/admin/time-clock?handler=ClockIn\"", html);

        // Two taps at once: the unique open-shift index keeps one shift, and both taps get the same friendly answer.
        var taps = await Task.WhenAll(PunchAsync(client, "ClockIn", AtTheKitchen()), PunchAsync(client, "ClockIn", AtTheKitchen()));
        Assert.All(taps, tap => AssertRedirectedTo(tap, Page));
        var open = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.True(open.IsOpen);
        Assert.Equal(LocationStatus.Captured, open.ClockInLocationStatus);
        Assert.Equal(32.7768, open.ClockInLatitude);
        Assert.Equal(-96.7971, open.ClockInLongitude);
        Assert.Equal(12.5, open.ClockInAccuracyMeters);
        Assert.Null(open.ClockInDistanceMeters); // the shared app has no kitchen location
        Assert.Equal(employee.FullName, open.EmployeeName);
        Assert.InRange(open.ClockInUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));

        html = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("You're on the clock", html);
        Assert.Contains("Clock out</span> <span aria-hidden=\"true\">·</span> <span class=\"bn\" lang=\"bn\">কাজ শেষ", html);
        Assert.Contains("action=\"/admin/time-clock?handler=ClockOut\"", html);

        // Clocking out, then the same tap again: the first result is shown, nothing changes.
        AssertRedirectedTo(await PunchAsync(client, "ClockOut", AtTheKitchen()), Page);
        Assert.Contains("Clocked out at", WebUtility.HtmlDecode(await client.GetStringAsync(Page)));
        AssertRedirectedTo(await PunchAsync(client, "ClockOut", NoLocation("Timeout")), Page);
        var closed = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal(ClockOutSource.Self, closed.ClockOutSource);
        Assert.Equal(LocationStatus.Captured, closed.ClockOutLocationStatus);
        Assert.False(closed.NeedsReview);

        // A clock-out with no shift open says so, on the page, without a server error.
        var someoneElse = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee, [])).Email);
        var refused = await PunchAsync(someoneElse, "ClockOut", AtTheKitchen());
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("You're not clocked in.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ClockingInAgainLater_OrWithoutScripts_IsHandledGracefully(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);

        // Without scripts the form posts no location: the shift is recorded and flagged for a manager.
        var html = await client.GetStringAsync(Page);
        AssertRedirectedTo(await PostFormAsync(client, $"{Page}?handler=ClockIn", html, new Dictionary<string, string> { ["Punch.Status"] = "NotProvided" }), Page);
        var shift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal(LocationStatus.NotProvided, shift.ClockInLocationStatus);
        Assert.True(shift.NeedsReview);
        Assert.Contains("No location was recorded", WebUtility.HtmlDecode(await client.GetStringAsync(Page)));

        // Forged numbers are refused before they reach the database.
        var other = await CreateStaffAsync(app, StaffRole.Employee, []);
        var otherClient = await SignInAsync(app, other.Email);
        var forged = await PunchAsync(otherClient, "ClockIn", new Dictionary<string, string>
        {
            ["Punch.Status"] = "Captured", ["Punch.Latitude"] = "NaN", ["Punch.Longitude"] = "-96.8", ["Punch.Accuracy"] = "10",
        });
        Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        Assert.Contains("could not be read", WebUtility.HtmlDecode(await forged.Content.ReadAsStringAsync()));
        Assert.Empty(await ShiftsOfAsync(app, other.UserId));

        // An hour after clocking in, another clock-in is a friendly message, not a second shift.
        await AddOpenShiftAsync(app, other, Now().AddHours(-1));
        var again = await PunchAsync(otherClient, "ClockIn", AtTheKitchen());
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("You're already clocked in since", WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync()));
        Assert.Single(await ShiftsOfAsync(app, other.UserId));
    }

    // ---- Forgotten clock-outs ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AForgottenClockOut_IsFixedByThePersonThemselves_ThenTheyClockIn(string provider)
    {
        var app = servers.Get(provider);
        var clock = app.Services.GetRequiredService<IDateTimeProvider>();
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);
        var forgotten = await AddOpenShiftAsync(app, employee, Now().AddHours(-20));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("You didn't clock out on", html);
        Assert.Contains("When did you leave?", html);
        Assert.Contains("I don't remember", html);
        Assert.DoesNotContain("handler=ClockOut", html);

        // A plain clock-out (an old page) is refused: they are asked when they left instead.
        var plain = await PunchAsync(client, "ClockOut", AtTheKitchen());
        Assert.Contains("Please tell us when you left first.", WebUtility.HtmlDecode(await plain.Content.ReadAsStringAsync()));

        var leftAtUtc = forgotten.ClockInUtc.AddHours(8);
        var leftAt = LocalInput(clock.ToBusinessTime(leftAtUtc));
        var fixedIt = await PostFormAsync(client, $"{Page}?handler=FixForgotten", await client.GetStringAsync(Page), new Dictionary<string, string> { ["Forgotten.LeftAt"] = leftAt });
        AssertRedirectedTo(fixedIt, Page);

        var shift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal(ClockOutSource.SelfCorrected, shift.ClockOutSource);
        Assert.True(shift.NeedsReview);
        Assert.Equal(BusinessTime.FromLocalInput(DateTime.ParseExact(leftAt, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture), clock.BusinessTimeZone), shift.ClockOutUtc);
        Assert.Equal(employee.Email, shift.EditedBy);
        var audit = await AuditOfAsync(app, employee.UserId);
        Assert.Contains(audit, e => e.Action == StaffAuditActions.ShiftSelfCorrected && e.Details!.StartsWith($"Shift #{shift.Id} ("));

        html = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("You can clock in now.", html);
        AssertRedirectedTo(await PunchAsync(client, "ClockIn", AtTheKitchen()), Page);
        Assert.Equal(2, (await ShiftsOfAsync(app, employee.UserId)).Count);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task IDontRemember_ClosesTheShiftWithoutHours_UntilAManagerFixesIt(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);
        await AddOpenShiftAsync(app, employee, DaysAgo(app, 2, 9));

        var html = await client.GetStringAsync(Page);
        AssertRedirectedTo(await PostFormAsync(client, $"{Page}?handler=FixForgotten", html, new Dictionary<string, string> { ["Forgotten.DontRemember"] = "true" }), Page);

        var shift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal(ClockOutSource.Missed, shift.ClockOutSource);
        Assert.Equal(shift.ClockInUtc, shift.ClockOutUtc);
        Assert.True(shift.NeedsReview);
        Assert.False(shift.CountsTowardsHours);
        Assert.Contains("A manager will fill in when you left.", WebUtility.HtmlDecode(await client.GetStringAsync(Page)));

        // The manager learns they worked 7:00 to 9:00, so the clock-out typed is the stored placeholder: the shift now counts.
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        AssertRedirectedTo(await CorrectAsync(admin, shift.Id, DaysAgoLocal(app, 2, 7), DaysAgoLocal(app, 2, 9), "asked them"), $"{Page}/shifts/{shift.Id}");
        var fixedShift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal((DaysAgo(app, 2, 7), DaysAgo(app, 2, 9)), (fixedShift.ClockInUtc, fixedShift.ClockOutUtc!.Value));
        Assert.Equal(ClockOutSource.Manager, fixedShift.ClockOutSource);
        Assert.True(fixedShift.CountsTowardsHours);
        Assert.True(fixedShift.NeedsReview); // edited: on the review list until someone marks it reviewed
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ANightShiftPastMidnight_EndsWithOneTap_WithoutBeingCalledForgotten(string provider)
    {
        // The app's clock reads 4:00 AM business time, whenever the test runs.
        await using var app = new AppFactory(provider, configureServices: services =>
        {
            var real = services.Last(d => d.ServiceType == typeof(IDateTimeProvider));
            services.AddSingleton<IDateTimeProvider>(sp => new EarlyMorningClock((IDateTimeProvider)ActivatorUtilities.CreateInstance(sp, real.ImplementationType!)));
        });
        var clock = app.Services.GetRequiredService<IDateTimeProvider>();
        Assert.Equal(4, clock.BusinessNow.Hour);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);
        var night = await AddOpenShiftAsync(app, employee, clock.ToUtc(clock.BusinessNow.Date.AddHours(-2))); // 10:00 PM yesterday

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("Your shift started", html);
        Assert.Contains("Are you finishing now?", html);
        Assert.Contains("I'm finishing now</span> <span aria-hidden=\"true\">·</span> <span class=\"bn\" lang=\"bn\">কাজ শেষ", html);
        Assert.Contains("Finished earlier? I finished at", html);
        Assert.DoesNotContain("You didn't clock out", html);

        // The "Clock out" of a page opened before midnight asks to confirm, without saying they forgot.
        var plain = WebUtility.HtmlDecode(await (await PunchAsync(client, "ClockOut", AtTheKitchen())).Content.ReadAsStringAsync());
        Assert.Contains("before midnight, so please confirm when you finish", plain);
        Assert.DoesNotContain("You didn't clock out", plain);

        // "I'm finishing now" is a clock-out form like the "Clock out" button: the page's script fills in the location.
        var finishing = FinishingNowForm().Match(html);
        Assert.True(finishing.Success, "The \"I'm finishing now\" form is not a clock-out form with the location fields.");
        Assert.Contains("data-clock-form", finishing.Value);
        Assert.Contains("name=\"Punch.Latitude\"", finishing.Value);
        Assert.Contains("data-skip-location", finishing.Value);

        // One tap ends it now, with the location; a second tap (a double tap) shows the same result.
        var finished = new Dictionary<string, string>(AtTheKitchen()) { ["Punch.ConfirmedPastMidnight"] = "true" };
        AssertRedirectedTo(await PostFormAsync(client, $"{Page}?handler=ClockOut", html, finished), Page);
        var shift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.Equal((night.Id, ClockOutSource.Self, LocationStatus.Captured), (shift.Id, shift.ClockOutSource, shift.ClockOutLocationStatus));
        Assert.NotNull(shift.ClockOutLatitude);
        Assert.Null(shift.EditedBy); // a punch, not an edit
        Assert.InRange(shift.ClockOutUtc!.Value, clock.UtcNow.AddMinutes(-1), clock.UtcNow);
        Assert.True(shift.CountsTowardsHours);
        Assert.True(shift.NeedsReview);
        var after = WebUtility.HtmlDecode(await client.GetStringAsync(Page));
        Assert.Contains("Your location was recorded", after);
        Assert.Contains("A manager checks shifts that run past midnight.", after);

        AssertRedirectedTo(await PostFormAsync(client, $"{Page}?handler=ClockOut", html, finished), Page);
        Assert.Equal(shift.ClockOutUtc, Assert.Single(await ShiftsOfAsync(app, employee.UserId)).ClockOutUtc);
        var audit = await AuditOfAsync(app, employee.UserId);
        Assert.Contains(audit, e => e.Action == StaffAuditActions.ShiftPastMidnight
            && e.Details!.StartsWith($"Shift #{shift.Id} (") && e.Details.Contains("shift ran past midnight"));

        // Managers see a punch with its location, not a forgotten clock-out typed in later.
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var shiftPage = WebUtility.HtmlDecode(await admin.GetStringAsync($"/admin/time-clock/shifts/{shift.Id}"));
        Assert.DoesNotContain("entered later by the employee", shiftPage);
        Assert.Contains("Clocked out after midnight", shiftPage);
    }

    // ---- Who sees and fixes what ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task Employees_SeeOnlyTheirOwnShifts(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var colleague = await CreateStaffAsync(app, StaffRole.Employee, []);
        var mine = await AddClosedShiftAsync(app, employee, DaysAgo(app, 3, 9), DaysAgo(app, 3, 17));
        var theirs = await AddClosedShiftAsync(app, colleague, DaysAgo(app, 3, 9), DaysAgo(app, 3, 17));
        var client = await SignInAsync(app, employee.Email);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Page}/shifts/{mine.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Page}/shifts/{theirs.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Page}/shifts/{int.MaxValue}")).StatusCode);

        AssertDenied(await client.GetAsync($"{Page}/team"));
        AssertDenied(await client.GetAsync($"{Page}/team?tab=weekly"));
        AssertDenied(await client.GetAsync($"{Page}/team?handler=ExportShifts"));
        AssertDenied(await client.GetAsync($"{Page}/team?handler=ExportWeekly"));
        AssertDenied(await client.GetAsync($"{Page}/add"));

        // Not even their own shift: fixing shifts needs timeclock.manage.
        var shiftPage = await client.GetStringAsync($"{Page}/shifts/{mine.Id}");
        Assert.DoesNotContain("handler=Correct", shiftPage);
        AssertDenied(await PostFormAsync(client, $"{Page}/shifts/{mine.Id}?handler=Correct", shiftPage, new Dictionary<string, string>
        {
            ["Version"] = "0", ["Times.ClockIn"] = LocalInput(DaysAgoLocal(app, 3, 7)), ["Times.ClockOut"] = LocalInput(DaysAgoLocal(app, 3, 20)), ["Times.Reason"] = "more pay",
        }));
        Assert.Equal(mine.ClockInUtc, (await ShiftsOfAsync(app, employee.UserId)).Single().ClockInUtc);
        AssertDenied(await PostFormAsync(client, $"{Page}/team?handler=KitchenLocation", shiftPage, new Dictionary<string, string> { ["latitude"] = "1", ["longitude"] = "2" }));

        AssertSignInRequired(await CreateClient(app).GetAsync(Page));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AManagerWhoSeesTheTeam_SeesEveryone_AndExportsSafeCsvFiles(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.TimeClockViewAll]);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var working = await CreateStaffAsync(app, StaffRole.Employee, []);
        await AddOpenShiftAsync(app, working, Now().AddHours(-1));
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        // An admin adds a past shift; its reason starts like a spreadsheet formula.
        var day = DaysAgoLocal(app, 2, 0).Date;
        var added = await AddShiftAsync(admin, employee, day.AddHours(9), day.AddHours(17).AddMinutes(30), "=HYPERLINK(\"http://evil.test\",\"click\")");
        AssertRedirectedTo(added, $"{Page}/shifts/", prefix: true);

        var client = await SignInAsync(app, manager.Email);
        var team = WebUtility.HtmlDecode(await client.GetStringAsync($"{Page}/team"));
        Assert.Contains("On the clock now", team);
        Assert.Contains(working.FullName, team);
        Assert.Contains(employee.FullName, team); // in the person filter
        Assert.Contains("Needs review", team);
        Assert.DoesNotContain("Use my current location as the kitchen", team); // admins only
        Assert.DoesNotContain("href=\"/admin/time-clock/add\"", team); // fixing shifts needs timeclock.manage

        var weekly = WebUtility.HtmlDecode(await client.GetStringAsync($"{Page}/team?tab=weekly&user={employee.UserId}"));
        Assert.Contains("8.50", weekly);
        Assert.Contains("8:30", weekly);

        var from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var export = await client.GetAsync($"{Page}/team?handler=ExportShifts&user={employee.UserId}&from={from}&to={from}");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.True(export.Headers.CacheControl!.NoStore);
        Assert.Equal("attachment", export.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal($"time-clock-shifts-{from}-to-{from}.csv", export.Content.Headers.ContentDisposition.FileNameStar ?? export.Content.Headers.ContentDisposition.FileName);
        Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);
        var lines = await CsvLinesAsync(export);
        Assert.Equal(
            "Name,Email,Date,Clock in,Clock out,Hours,Status,Clock-in location,Clock-out location,Clock-in distance (m),Clock-out distance (m),"
            + "Clock-in coordinates,Clock-out coordinates,Edited by,Reason,Voided",
            lines[0]);
        var row = Assert.Single(lines.Skip(1));
        Assert.StartsWith($"{employee.FullName},{employee.Email},{from},{from} 09:00,{from} 17:30,8.50,Closed,Entered by hand,Entered by hand,", row);
        Assert.EndsWith($",{AppFactory.AdminEmail},\"'=HYPERLINK(\"\"http://evil.test\"\",\"\"click\"\")\",", row);

        var weeklyExport = await client.GetAsync($"{Page}/team?handler=ExportWeekly&user={employee.UserId}&from={from}&to={from}");
        Assert.True(weeklyExport.Headers.CacheControl!.NoStore);
        Assert.Equal("attachment", weeklyExport.Content.Headers.ContentDisposition!.DispositionType);
        var weeklyLines = await CsvLinesAsync(weeklyExport);
        Assert.Equal("Week starting,Week ending,Name,Email,Hours,Hours (h:mm),Over 40 h,Shifts,Shifts needing review,On the clock now (not counted)", weeklyLines[0]);
        Assert.Contains(weeklyLines, l => l.Contains($",{employee.FullName},{employee.Email},8.50,8:30,,1,1,", StringComparison.Ordinal)); // added by hand: needs a review

        // Seeing the team is not fixing it.
        AssertDenied(await client.GetAsync($"{Page}/add"));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task YearTyposAndPagesPastTheEnd_ShowWhatThereIs_AndShiftsBefore2000_AreRefused(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.TimeClockViewAll]);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var shift = await AddClosedShiftAsync(app, employee, DaysAgo(app, 6, 9), DaysAgo(app, 6, 17));
        var client = await SignInAsync(app, manager.Email);

        string[] filters =
        [
            "from=0202-10-01&to=2026-10-11", "from=1700-01-01", "show=review&from=0202-10-01", "to=9999-12-31", "from=9999-12-31",
            "from=0001-01-01&to=0001-01-01", "tab=weekly&to=9999-12-31", "tab=weekly&from=0001-01-01&to=0001-01-01", "p=50000000", "p=2147483647",
        ];
        foreach (var filter in filters)
        {
            foreach (var handler in new[] { string.Empty, "&handler=ExportShifts", "&handler=ExportWeekly" })
            {
                var response = await client.GetAsync($"{Page}/team?{filter}{handler}");
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"{filter}{handler}: {(int)response.StatusCode}");
            }
        }

        // From the start until "9999": everything there is.
        const string everything = "from=0202-10-01&to=9999-12-31";
        Assert.Contains($"/shifts/{shift.Id}\"", await client.GetStringAsync($"{Page}/team?user={employee.UserId}&{everything}"));
        Assert.Equal(2, (await CsvLinesAsync(await client.GetAsync($"{Page}/team?handler=ExportShifts&user={employee.UserId}&{everything}"))).Count);
        Assert.Contains(
            await CsvLinesAsync(await client.GetAsync($"{Page}/team?handler=ExportWeekly&user={employee.UserId}&{everything}")),
            l => l.Contains($",{employee.Email},8.00,", StringComparison.Ordinal));

        // A shift typed with such a year is refused with a message, not a database error.
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var added = await AddShiftAsync(admin, employee, new DateTime(202, 10, 1, 9, 0, 0), new DateTime(202, 10, 1, 17, 0, 0), "typo");
        Assert.Contains("Please check the year: a shift can't start before 2000.", WebUtility.HtmlDecode(await added.Content.ReadAsStringAsync()));
        var corrected = await CorrectAsync(admin, shift.Id, new DateTime(1700, 1, 1, 9, 0, 0), new DateTime(1700, 1, 1, 17, 0, 0), "typo");
        Assert.Contains("Please check the year: a shift can't start before 2000.", WebUtility.HtmlDecode(await corrected.Content.ReadAsStringAsync()));
        Assert.Equal(shift.ClockInUtc, (await ShiftsOfAsync(app, employee.UserId)).Single().ClockInUtc);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task AManager_FixesOtherPeoplesShifts_ButNotTheirOwn(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.TimeClockViewAll, Permissions.TimeClockManage]);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var own = await AddClosedShiftAsync(app, manager, DaysAgo(app, 4, 9), DaysAgo(app, 4, 17));
        var theirs = await AddClosedShiftAsync(app, employee, DaysAgo(app, 4, 9), DaysAgo(app, 4, 17));
        var client = await SignInAsync(app, manager.Email);

        var ownPage = WebUtility.HtmlDecode(await client.GetStringAsync($"{Page}/shifts/{own.Id}"));
        Assert.Contains("Ask an admin to fix your own shifts.", ownPage);
        Assert.DoesNotContain("handler=Correct", ownPage);
        var refused = await CorrectAsync(client, own.Id, DaysAgoLocal(app, 4, 7), DaysAgoLocal(app, 4, 19), "worked longer");
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Ask an admin to fix your own shifts.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.Equal(own.ClockInUtc, (await ShiftsOfAsync(app, manager.UserId)).Single().ClockInUtc);

        AssertRedirectedTo(await CorrectAsync(client, theirs.Id, DaysAgoLocal(app, 4, 8, 30), DaysAgoLocal(app, 4, 17), "forgot to clock in"), $"{Page}/shifts/{theirs.Id}");
        var corrected = (await ShiftsOfAsync(app, employee.UserId)).Single();
        Assert.Equal(DaysAgo(app, 4, 8, 30), corrected.ClockInUtc);
        Assert.Equal(theirs.ClockInUtc, corrected.OriginalClockInUtc);
        Assert.Equal(theirs.ClockOutUtc, corrected.OriginalClockOutUtc);
        Assert.Equal(manager.Email, corrected.EditedBy);
        Assert.Equal("forgot to clock in", corrected.EditReason);
        Assert.Contains(await AuditOfAsync(app, employee.UserId), e => e.Action == StaffAuditActions.ShiftCorrected && e.ChangedBy == manager.Email);

        // The page the manager had open is now out of date: a second save is refused instead of overwriting.
        var stale = await CorrectAsync(client, theirs.Id, DaysAgoLocal(app, 4, 8), DaysAgoLocal(app, 4, 17), "again", version: theirs.Version);
        Assert.Contains("Someone changed this shift", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));

        // An edited shift needs a review (like one added by hand) until someone marks it reviewed.
        Assert.True(corrected.NeedsReview);
        Assert.Contains($"/shifts/{theirs.Id}\"", await client.GetStringAsync($"{Page}/team?show=review&user={employee.UserId}"));
        var shiftPage = await client.GetStringAsync($"{Page}/shifts/{theirs.Id}");
        AssertRedirectedTo(
            await PostFormAsync(client, $"{Page}/shifts/{theirs.Id}?handler=Reviewed", shiftPage, new Dictionary<string, string> { ["Version"] = VersionOn(shiftPage) }),
            $"{Page}/shifts/{theirs.Id}");
        Assert.False((await ShiftsOfAsync(app, employee.UserId)).Single().NeedsReview);
        Assert.DoesNotContain($"/shifts/{theirs.Id}\"", await client.GetStringAsync($"{Page}/team?show=review&user={employee.UserId}"));
        Assert.Contains(await AuditOfAsync(app, employee.UserId), e => e.Action == "ShiftReviewed" && e.ChangedBy == manager.Email);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheShiftPage_KeepsTheShiftsTimesAfterAFailedAction_AndEveryShiftKeepsItsHistory(string provider)
    {
        var app = servers.Get(provider);
        var clock = app.Services.GetRequiredService<IDateTimeProvider>();
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        // Closing an open shift at an impossible time: the error shows, and "Correct times" still holds the shift's times.
        var open = await AddOpenShiftAsync(app, employee, Now().AddHours(-3));
        var openPage = await admin.GetStringAsync($"{Page}/shifts/{open.Id}");
        var refused = await PostFormAsync(admin, $"{Page}/shifts/{open.Id}?handler=Close", openPage, new Dictionary<string, string>
        {
            ["Version"] = VersionOn(openPage),
            ["Closing.ClockOut"] = LocalInput(clock.ToBusinessTime(open.ClockInUtc).AddHours(-1)),
            ["Closing.Reason"] = "forgot to clock out",
        });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var html = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        Assert.Contains("must be after the clock-in", html);
        Assert.Contains($"name=\"Times.ClockIn\" value=\"{LocalInput(clock.ToBusinessTime(open.ClockInUtc))}\"", html);

        // The same after a void without a reason, on a closed shift (both times).
        var closed = await AddClosedShiftAsync(app, employee, DaysAgo(app, 3, 9), DaysAgo(app, 3, 17));
        var closedPage = await admin.GetStringAsync($"{Page}/shifts/{closed.Id}");
        refused = await PostFormAsync(admin, $"{Page}/shifts/{closed.Id}?handler=Void", closedPage, new Dictionary<string, string>
        {
            ["Version"] = VersionOn(closedPage), ["Voiding.Reason"] = "",
        });
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        html = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        Assert.Contains($"name=\"Times.ClockIn\" value=\"{LocalInput(DaysAgoLocal(app, 3, 9))}\"", html);
        Assert.Contains($"name=\"Times.ClockOut\" value=\"{LocalInput(DaysAgoLocal(app, 3, 17))}\"", html);
        Assert.False((await ShiftsOfAsync(app, employee.UserId)).Single(s => s.Id == closed.Id).IsVoided);

        // A correction, then many newer entries about the same person: the shift's page still lists its own.
        AssertRedirectedTo(await CorrectAsync(admin, closed.Id, DaysAgoLocal(app, 3, 8), DaysAgoLocal(app, 3, 17), "came in early"), $"{Page}/shifts/{closed.Id}");
        await using (var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync())
        {
            await connection.ExecuteAsync(
                "INSERT INTO StaffAuditLog (TargetUserId, TargetName, Action, Details, ChangedBy, ChangedAtUtc) VALUES (@UserId, 'Busy', 'ShiftReviewed', @Details, 'tests', @At)",
                Enumerable.Range(0, 250).Select(i => new { employee.UserId, Details = $"Shift #{closed.Id}0 (Mon): unrelated entry {i}", At = DateTime.UtcNow.AddSeconds(1) }));
        }

        html = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/shifts/{closed.Id}"));
        Assert.Contains("<strong>Shift corrected</strong>", html);
        Assert.Contains("Reason: came in early", html);
        Assert.DoesNotContain("unrelated entry", html);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task OverlappingSkippedAndFutureTimes_AreRefused(string provider)
    {
        var app = servers.Get(provider);
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        var day = DaysAgoLocal(app, 5, 0).Date;

        AssertRedirectedTo(await AddShiftAsync(admin, employee, day.AddHours(9), day.AddHours(17), "phone died"), $"{Page}/shifts/", prefix: true);

        var overlap = await AddShiftAsync(admin, employee, day.AddHours(16), day.AddHours(20), "evening event");
        Assert.Equal(HttpStatusCode.OK, overlap.StatusCode);
        Assert.Contains("Those times overlap another shift of this person", WebUtility.HtmlDecode(await overlap.Content.ReadAsStringAsync()));

        // Dallas moved its clocks from 2:00 to 3:00 AM on March 8, 2026.
        var skipped = await AddShiftAsync(admin, employee, new DateTime(2026, 3, 8, 2, 30, 0), new DateTime(2026, 3, 8, 9, 0, 0), "night prep");
        Assert.Contains("That time doesn't exist (clocks moved forward).", WebUtility.HtmlDecode(await skipped.Content.ReadAsStringAsync()));

        var tomorrow = DaysAgoLocal(app, -1, 0).Date;
        var future = await AddShiftAsync(admin, employee, tomorrow.AddHours(9), tomorrow.AddHours(17), "planned");
        Assert.Contains("A shift can't start in the future.", WebUtility.HtmlDecode(await future.Content.ReadAsStringAsync()));

        var tooLong = await AddShiftAsync(admin, employee, day.AddDays(-2).AddHours(6), day.AddDays(-1).AddHours(7), "double shift");
        Assert.Contains("at most 24 hours", WebUtility.HtmlDecode(await tooLong.Content.ReadAsStringAsync()));

        Assert.Single(await ShiftsOfAsync(app, employee.UserId));
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task ShiftsAddedAndCorrectedAtTheSameTime_AreSavedOneAfterTheOther_WithoutOverlaps(string provider)
    {
        var app = servers.Get(provider);
        var first = await CreateStaffAsync(app, StaffRole.Employee, []);
        var second = await CreateStaffAsync(app, StaffRole.Employee, []);

        for (var round = 0; round < 8; round++)
        {
            var day = 20 + (round * 3);
            var corrected = await AddClosedShiftAsync(app, second, DaysAgo(app, day + 2, 9), DaysAgo(app, day + 2, 12));

            // Two managers adding one person's shifts on different days, two adding another person's over the same hours,
            // and a correction racing an added shift into the same hours: nothing fails, and no overlap is ever saved.
            var outcomes = await Task.WhenAll(
                TryAddShiftAsync(app, first, DaysAgo(app, day, 9), DaysAgo(app, day, 17)),
                TryAddShiftAsync(app, first, DaysAgo(app, day + 1, 9), DaysAgo(app, day + 1, 17)),
                TryAddShiftAsync(app, second, DaysAgo(app, day, 10), DaysAgo(app, day, 18)),
                TryAddShiftAsync(app, second, DaysAgo(app, day, 12), DaysAgo(app, day, 20)),
                ChangeTimesAsync(app, corrected, DaysAgo(app, day + 2, 9), DaysAgo(app, day + 2, 17)),
                TryAddShiftAsync(app, second, DaysAgo(app, day + 2, 13), DaysAgo(app, day + 2, 15)));

            Assert.Equal([ShiftChangeOutcome.Saved, ShiftChangeOutcome.Saved], outcomes[..2]);
            Assert.Single(outcomes[2..4], o => o == ShiftChangeOutcome.Saved);
            Assert.Single(outcomes[4..], o => o == ShiftChangeOutcome.Saved);
            Assert.DoesNotContain(ShiftChangeOutcome.Changed, outcomes);
        }

        Assert.Equal(16, (await ShiftsOfAsync(app, first.UserId)).Count);
        var shifts = (await ShiftsOfAsync(app, second.UserId)).OrderBy(s => s.ClockInUtc).ToList();
        Assert.All(shifts.Zip(shifts.Skip(1)), pair => Assert.True(pair.First.ClockOutUtc <= pair.Second.ClockInUtc));
    }

    // ---- Hours ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task WeeklyHours_CutShiftsAtTheWeeksMidnight_AndLeaveOutVoidedShifts(string provider)
    {
        var app = servers.Get(provider);
        var clock = app.Services.GetRequiredService<IDateTimeProvider>();
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var admin = await SignInAsync(app, AppFactory.AdminEmail);

        // Workweeks start on Monday (the default). Two weeks ago: Sunday 10 PM → Monday 4 AM, Tuesday 9–5, Wednesday 9–12 (voided).
        var monday = BusinessTime.WeekStart(DateOnly.FromDateTime(clock.BusinessNow), DayOfWeek.Monday).AddDays(-14).ToDateTime(TimeOnly.MinValue);
        AssertRedirectedTo(await AddShiftAsync(admin, employee, monday.AddHours(-2), monday.AddHours(4), "night prep"), $"{Page}/shifts/", prefix: true);
        AssertRedirectedTo(await AddShiftAsync(admin, employee, monday.AddDays(1).AddHours(9), monday.AddDays(1).AddHours(17), "phone died"), $"{Page}/shifts/", prefix: true);
        var mistake = await AddShiftAsync(admin, employee, monday.AddDays(2).AddHours(9), monday.AddDays(2).AddHours(12), "typo");
        var mistakeId = int.Parse(mistake.Headers.Location!.OriginalString.Split('/').Last(), CultureInfo.InvariantCulture);

        var shiftPage = await admin.GetStringAsync($"{Page}/shifts/{mistakeId}");
        AssertRedirectedTo(await PostFormAsync(admin, $"{Page}/shifts/{mistakeId}?handler=Void", shiftPage, new Dictionary<string, string>
        {
            ["Version"] = VersionOn(shiftPage), ["Voiding.Reason"] = "entered twice",
        }), $"{Page}/shifts/{mistakeId}");
        Assert.Contains("This shift was voided by", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/shifts/{mistakeId}")));
        Assert.Contains(await AuditOfAsync(app, employee.UserId), e => e.Action == StaffAuditActions.ShiftVoided);

        var from = monday.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = monday.AddDays(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = await CsvLinesAsync(await admin.GetAsync($"{Page}/team?handler=ExportWeekly&user={employee.UserId}&from={from}&to={to}"));
        Assert.Equal(3, lines.Count);
        Assert.StartsWith($"{from},{monday.AddDays(-1):yyyy-MM-dd},{employee.FullName},{employee.Email},2.00,2:00,,1,1,", lines[1]); // added by hand: needs a review
        Assert.StartsWith($"{monday:yyyy-MM-dd},{to},{employee.FullName},{employee.Email},12.00,12:00,,2,2,", lines[2]);

        // The shift list for those two weeks counts the same 14 hours and shows the voided shift struck through.
        var list = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/team?user={employee.UserId}&from={from}&to={to}"));
        Assert.Contains("Hours for these dates: <strong>14.00 h (14:00)</strong>", list);
        Assert.Contains("row-muted voided", list);
    }

    // ---- Settings and the kitchen location (apps of their own: they change site-wide settings) ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheTimeClockSettings_AreForAdmins_AndAdminsCanUseTheirLocationAsTheKitchen(string provider)
    {
        await using var app = new AppFactory(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.TimeClockViewAll, Permissions.TimeClockManage]);
        AssertDenied(await (await SignInAsync(app, manager.Email)).GetAsync("/admin/settings/time-clock"));

        var admin = await SignInAsync(app, AppFactory.AdminEmail);
        Assert.Contains("href=\"/admin/settings/time-clock\"", await admin.GetStringAsync("/admin/settings"));
        var html = await admin.GetStringAsync("/admin/settings/time-clock");
        var saved = await PostFormAsync(admin, "/admin/settings/time-clock", html, new Dictionary<string, string>
        {
            ["__revision"] = RevisionOn(html),
            ["TimeClock:KitchenLatitude"] = "",
            ["TimeClock:KitchenLongitude"] = "",
            ["TimeClock:NearKitchenRadiusMeters"] = "450",
            ["TimeClock:WorkweekStartsOn"] = "Sunday",
            ["TimeClock:RequireLocation"] = "true",
        });
        AssertRedirectedTo(saved, "/admin/settings/time-clock?saved=1");
        var options = app.Services.GetRequiredService<IOptionsMonitor<TimeClockOptions>>().CurrentValue;
        Assert.Equal((450, DayOfWeek.Sunday, true, false), (options.NearKitchenRadiusMeters, options.WorkweekStartsOn, options.RequireLocation, options.HasKitchenLocation));

        // Team page: the admin stands in the kitchen and taps "Use my current location as the kitchen".
        var team = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/team"));
        Assert.Contains("The kitchen location isn't set", team);
        Assert.Contains("Use my current location as the kitchen", team);
        var kitchen = await PostFormAsync(admin, $"{Page}/team?handler=KitchenLocation", team, new Dictionary<string, string>
        {
            ["latitude"] = "32.9123456789", ["longitude"] = "-96.6543210987", ["accuracy"] = "18",
        });
        AssertRedirectedTo(kitchen, $"{Page}/team");
        options = app.Services.GetRequiredService<IOptionsMonitor<TimeClockOptions>>().CurrentValue;
        Assert.Equal((32.912346, -96.654321), (options.KitchenLatitude, options.KitchenLongitude));
        Assert.Equal((450, DayOfWeek.Sunday, true), (options.NearKitchenRadiusMeters, options.WorkweekStartsOn, options.RequireLocation));
        Assert.Contains("The kitchen location is now where you are", WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/team")));
        Assert.Contains("Recent changes", await admin.GetStringAsync("/admin/settings/time-clock"));

        // A location that can't be read (blocked on the device): the help for turning it on is on this page too.
        AssertRedirectedTo(await PostFormAsync(admin, $"{Page}/team?handler=KitchenLocation", team, new Dictionary<string, string>
        {
            ["latitude"] = "", ["longitude"] = "", ["accuracy"] = "",
        }), $"{Page}/team");
        var unreadable = WebUtility.HtmlDecode(await admin.GetStringAsync($"{Page}/team"));
        Assert.Contains("see \"How to turn location on\" below the kitchen button", unreadable);
        Assert.Contains("id=\"location-help\"", unreadable);
        Assert.Contains("<summary>How to turn location on</summary>", unreadable);

        // A rough location is not good enough for the kitchen.
        var rough = await PostFormAsync(admin, $"{Page}/team?handler=KitchenLocation", team, new Dictionary<string, string>
        {
            ["latitude"] = "33.5", ["longitude"] = "-97.1", ["accuracy"] = "2500",
        });
        AssertRedirectedTo(rough, $"{Page}/team");
        Assert.Equal(32.912346, app.Services.GetRequiredService<IOptionsMonitor<TimeClockOptions>>().CurrentValue.KitchenLatitude);

        // Managers may manage shifts but not settings.
        var managerClient = await SignInAsync(app, manager.Email);
        AssertDenied(await PostFormAsync(managerClient, $"{Page}/team?handler=KitchenLocation", await managerClient.GetStringAsync($"{Page}/team"),
            new Dictionary<string, string> { ["latitude"] = "1", ["longitude"] = "2", ["accuracy"] = "5" }));
        AssertDenied(await PostFormAsync(managerClient, "/admin/settings/time-clock", await managerClient.GetStringAsync(Page), new Dictionary<string, string> { ["__revision"] = "0" }));
    }

    [Fact]
    public async Task ALiveSiteUpgrading_GetsTheTimeClockSettingsFromTheirDefaults_WithoutBeingAskedToCheckThem()
    {
        var directory = AppFactory.NewDirectory();
        try
        {
            // A site that already has business data, from before the time clock existed.
            await using (var app = new AppFactory("Sqlite", directory: directory))
            {
                await ExecuteAsync(app, "INSERT INTO CateringInquiries (Name, Email, Message, CreatedAtUtc) VALUES ('Guest', 'guest@test.local', 'A wedding', '2026-10-01 12:00:00')");
                await ExecuteAsync(app, "DELETE FROM AppSettings WHERE [Key] LIKE 'TimeClock:%'");
                await ExecuteAsync(app, "DELETE FROM AppSettingsSections WHERE Section = 'TimeClock'");
            }

            var errors = new ErrorLog();
            await using (var app = new AppFactory("Sqlite", directory: directory, configureServices: services => services.AddSingleton<ILoggerProvider>(errors)))
            {
                var options = app.Services.GetRequiredService<IOptionsMonitor<TimeClockOptions>>().CurrentValue;
                Assert.Equal((300, DayOfWeek.Monday, false, false), (options.NearKitchenRadiusMeters, options.WorkweekStartsOn, options.RequireLocation, options.HasKitchenLocation));
                Assert.Empty(errors.Messages);

                var admin = await SignInAsync(app, AppFactory.AdminEmail);
                Assert.DoesNotContain("data-settings-review", await admin.GetStringAsync("/admin/settings/time-clock"));
                Assert.DoesNotContain("data-settings-review", await admin.GetStringAsync("/admin/settings"));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task WithTheKitchenSet_PunchesRecordTheDistance_AndRequiredLocationsAreEnforced(string provider)
    {
        await using var app = new AppFactory(provider, new Dictionary<string, string?>
        {
            ["TimeClock:KitchenLatitude"] = "32.7767",
            ["TimeClock:KitchenLongitude"] = "-96.797",
            ["TimeClock:RequireLocation"] = "true",
        });
        var employee = await CreateStaffAsync(app, StaffRole.Employee, []);
        var client = await SignInAsync(app, employee.Email);
        Assert.Contains("Your location is needed to clock in or out.", WebUtility.HtmlDecode(await client.GetStringAsync(Page)));

        var refused = await PunchAsync(client, "ClockIn", NoLocation("PermissionDenied"));
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        var page = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
        Assert.Contains("Your location is needed to clock in, but location is blocked for this site on your device.", page);
        Assert.Contains("<details class=\"location-help\" id=\"location-help\" data-location-help open=\"open\">", page);
        Assert.Empty(await ShiftsOfAsync(app, employee.UserId));

        // 0.01° of latitude north of the kitchen: about 1.1 km, so the clock-in is marked for review.
        AssertRedirectedTo(await PunchAsync(client, "ClockIn", AtTheKitchen(32.7867, -96.797)), Page);
        var shift = Assert.Single(await ShiftsOfAsync(app, employee.UserId));
        Assert.InRange(shift.ClockInDistanceMeters!.Value, 1100, 1125);
        Assert.True(shift.NeedsReview);
        Assert.Contains("from the kitchen", WebUtility.HtmlDecode(await client.GetStringAsync(Page)));

        var outRefused = await PunchAsync(client, "ClockOut", NoLocation("Timeout"));
        Assert.Contains("Your location is needed to clock out", WebUtility.HtmlDecode(await outRefused.Content.ReadAsStringAsync()));
        AssertRedirectedTo(await PunchAsync(client, "ClockOut", AtTheKitchen()), Page);
        Assert.InRange((await ShiftsOfAsync(app, employee.UserId)).Single().ClockOutDistanceMeters!.Value, 10, 20);
    }

    // ---- /clock ----

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task TheShortClockAddress_OpensTheTimeClock_AfterSigningIn_AndIsHiddenFromSearchEngines(string provider)
    {
        var app = servers.Get(provider);
        var visitor = CreateClient(app);

        var response = await visitor.GetAsync("/clock");
        AssertRedirectedTo(response, Page);
        AssertSignInRequired(await visitor.GetAsync(Page));
        Assert.Contains("Disallow: /clock", await visitor.GetStringAsync("/robots.txt"));

        var employee = await SignInAsync(app, (await CreateStaffAsync(app, StaffRole.Employee, [])).Email);
        AssertRedirectedTo(await employee.GetAsync("/clock"), Page);
    }

    [Theory]
    [MemberData(nameof(TestServers.Providers), MemberType = typeof(TestServers))]
    public async Task APostWithoutAKnownAction_ShowsThePageAgain(string provider)
    {
        var app = servers.Get(provider);
        var manager = await CreateStaffAsync(app, StaffRole.Manager, [Permissions.TimeClockViewAll]);
        var shift = await AddClosedShiftAsync(app, manager, DaysAgo(app, 3, 9), DaysAgo(app, 3, 17));
        var client = await SignInAsync(app, manager.Email);
        var html = await client.GetStringAsync(Page);

        foreach (var handler in new[] { string.Empty, "?handler=Bogus" })
        {
            AssertRedirectedTo(await PostFormAsync(client, $"{Page}{handler}", html, new Dictionary<string, string>()), Page);
            AssertRedirectedTo(await PostFormAsync(client, $"{Page}/shifts/{shift.Id}{handler}", html, new Dictionary<string, string>()), $"{Page}/shifts/{shift.Id}");
            Assert.Equal(HttpStatusCode.OK, (await PostFormAsync(client, $"{Page}/team{handler}", html, new Dictionary<string, string>())).StatusCode);
        }

        Assert.Equal(shift.Version, (await ShiftsOfAsync(app, manager.UserId)).Single().Version);
    }

    // ---- The agreed access of the time clock pages ----

    private static readonly IReadOnlyDictionary<string, string> TimeClockAccess = new Dictionary<string, string>
    {
        ["/Pages/Admin/TimeClock/Index.cshtml"] = "any staff",
        ["/Pages/Admin/TimeClock/Index.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Index.cshtml OnPost"] = "page rule",
        ["/Pages/Admin/TimeClock/Index.cshtml OnPostClockInAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Index.cshtml OnPostClockOutAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Index.cshtml OnPostFixForgottenAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Team.cshtml"] = Permissions.TimeClockViewAll,
        ["/Pages/Admin/TimeClock/Team.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Team.cshtml OnGetExportShiftsAsync"] = Permissions.TimeClockViewAll,
        ["/Pages/Admin/TimeClock/Team.cshtml OnGetExportWeeklyAsync"] = Permissions.TimeClockViewAll,
        ["/Pages/Admin/TimeClock/Team.cshtml OnPostKitchenLocationAsync"] = "admin",
        ["/Pages/Admin/TimeClock/Shift.cshtml"] = "any staff",
        ["/Pages/Admin/TimeClock/Shift.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Shift.cshtml OnPost"] = "page rule",
        ["/Pages/Admin/TimeClock/Shift.cshtml OnPostCorrectAsync"] = Permissions.TimeClockManage,
        ["/Pages/Admin/TimeClock/Shift.cshtml OnPostCloseAsync"] = Permissions.TimeClockManage,
        ["/Pages/Admin/TimeClock/Shift.cshtml OnPostVoidAsync"] = Permissions.TimeClockManage,
        ["/Pages/Admin/TimeClock/Shift.cshtml OnPostReviewedAsync"] = Permissions.TimeClockManage,
        ["/Pages/Admin/TimeClock/Add.cshtml"] = Permissions.TimeClockManage,
        ["/Pages/Admin/TimeClock/Add.cshtml OnGetAsync"] = "page rule",
        ["/Pages/Admin/TimeClock/Add.cshtml OnPostAsync"] = "page rule",
    };

    /// <summary>Pinned in both directions: exactly these time clock pages and handlers exist, each with exactly this rule.</summary>
    [Fact]
    public void TheTimeClockPages_HaveTheirAgreedAccess()
    {
        var pages = new RazorCompiledItemLoader().LoadItems(typeof(Program).Assembly)
            .Where(i => i.Kind == "mvc.1.0.razor-page" && i.Identifier.StartsWith("/Pages/Admin/TimeClock/", StringComparison.Ordinal))
            .ToDictionary(i => i.Identifier, i => i.Type.GetProperty("Model")!.PropertyType);

        var actual = new Dictionary<string, string>();
        foreach (var (path, model) in pages)
        {
            actual[path] = Describe(PermissionPageFilter.RulesFor(model).Single());
            foreach (var handler in model.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Where(m => m.Name.StartsWith("On", StringComparison.Ordinal)))
            {
                var rules = PermissionPageFilter.RulesFor(handler);
                actual[$"{path} {handler.Name}"] = rules.Length == 0 ? "page rule" : Describe(rules.Single());
            }
        }

        Assert.Equal(TimeClockAccess.OrderBy(e => e.Key), actual.OrderBy(e => e.Key));

        static string Describe(Attribute rule) => rule switch
        {
            RequirePermissionAttribute r => r.Permission,
            AdminOnlyAttribute => "admin",
            AnyStaffAttribute => "any staff",
            var other => other.ToString()!,
        };
    }

    [Fact]
    public void TheTimeClockRequests_StateWhoMaySendThem()
    {
        static string Rule(Type request) =>
            request.GetCustomAttribute<RequirePermissionAttribute>()?.Permission
            ?? (typeof(IRequireStaff).IsAssignableFrom(request) ? "any staff" : typeof(IRequireAdmin).IsAssignableFrom(request) ? "admin" : "none");

        // The caller's own clock: no user or shift id to tamper with.
        foreach (var own in new[] { typeof(ClockInCommand), typeof(ClockOutCommand), typeof(FixMyForgottenClockOutCommand), typeof(GetMyTimeClockQuery) })
        {
            Assert.Equal("any staff", Rule(own));
            Assert.DoesNotContain(own.GetProperties(), p => p.Name.Contains("User", StringComparison.Ordinal) || p.Name is "Id" or "ShiftId");
        }

        Assert.Equal("any staff", Rule(typeof(GetShiftQuery))); // returns nothing unless it is your shift or you see the team
        foreach (var team in new[] { typeof(GetTeamTimeClockQuery), typeof(GetShiftsQuery), typeof(GetWeeklyHoursQuery), typeof(ExportShiftsCsvQuery), typeof(ExportWeeklyHoursCsvQuery) })
        {
            Assert.Equal(Permissions.TimeClockViewAll, Rule(team));
        }

        foreach (var fix in new[] { typeof(CorrectShiftCommand), typeof(CloseShiftCommand), typeof(AddShiftCommand), typeof(VoidShiftCommand), typeof(MarkShiftReviewedCommand) })
        {
            Assert.Equal(Permissions.TimeClockManage, Rule(fix));
        }
    }

    // ---- Helpers ----

    private static DateTime Now()
    {
        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    /// <summary>A business-local time <paramref name="days"/> days ago (negative: ahead).</summary>
    private static DateTime DaysAgoLocal(AppFactory app, int days, int hour, int minute = 0) =>
        app.Services.GetRequiredService<IDateTimeProvider>().BusinessNow.Date.AddDays(-days).AddHours(hour).AddMinutes(minute);

    private static DateTime DaysAgo(AppFactory app, int days, int hour, int minute = 0) =>
        BusinessTime.FromLocalInput(DaysAgoLocal(app, days, hour, minute), app.Services.GetRequiredService<IDateTimeProvider>().BusinessTimeZone);

    private static string LocalInput(DateTime local) => local.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> AtTheKitchen(double latitude = 32.7768, double longitude = -96.7971) => new()
    {
        ["Punch.Status"] = "Captured",
        ["Punch.Latitude"] = latitude.ToString(CultureInfo.InvariantCulture),
        ["Punch.Longitude"] = longitude.ToString(CultureInfo.InvariantCulture),
        ["Punch.Accuracy"] = "12.5",
    };

    private static Dictionary<string, string> NoLocation(string status) => new() { ["Punch.Status"] = status };

    private static async Task<HttpResponseMessage> PunchAsync(HttpClient client, string handler, IDictionary<string, string> fields) =>
        await PostFormAsync(client, $"{Page}?handler={handler}", await client.GetStringAsync(Page), fields);

    private static async Task<HttpResponseMessage> AddShiftAsync(HttpClient client, TestStaff person, DateTime clockInLocal, DateTime clockOutLocal, string reason) =>
        await PostFormAsync(client, $"{Page}/add", await client.GetStringAsync($"{Page}/add"), new Dictionary<string, string>
        {
            ["Input.UserId"] = person.UserId,
            ["Input.ClockIn"] = LocalInput(clockInLocal),
            ["Input.ClockOut"] = LocalInput(clockOutLocal),
            ["Input.Reason"] = reason,
        });

    private static async Task<HttpResponseMessage> CorrectAsync(HttpClient client, int shiftId, DateTime clockInLocal, DateTime clockOutLocal, string reason, int? version = null)
    {
        var html = await client.GetStringAsync($"{Page}/shifts/{shiftId}");
        return await PostFormAsync(client, $"{Page}/shifts/{shiftId}?handler=Correct", html, new Dictionary<string, string>
        {
            ["Version"] = version?.ToString(CultureInfo.InvariantCulture) ?? VersionOn(html),
            ["Times.ClockIn"] = LocalInput(clockInLocal),
            ["Times.ClockOut"] = LocalInput(clockOutLocal),
            ["Times.Reason"] = reason,
        });
    }

    private static void AssertRedirectedTo(HttpResponseMessage response, string location, bool prefix = false)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Expected a redirect to {location}, got {(int)response.StatusCode}.");
        var actual = response.Headers.Location!.OriginalString;
        Assert.True(prefix ? actual.StartsWith(location, StringComparison.Ordinal) : actual == location, $"Redirected to {actual}, expected {location}.");
    }

    private static async Task<List<string>> CsvLinesAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        return [.. Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries)];
    }

    private static string VersionOn(string html) => VersionField().Match(html).Groups[1].Value;

    private static string RevisionOn(string html) => RevisionField().Match(html).Groups[1].Value;

    private static async Task<TimeClockShift> AddOpenShiftAsync(AppFactory app, TestStaff person, DateTime clockInUtc)
    {
        var shift = TimeClockShift.Open(person.UserId, person.FullName, PunchLocation.Captured(32.7767, -96.797, 10), null, null, false, clockInUtc);
        using var scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<ITimeClockRepository>().TryAddOpenShiftAsync(shift));
        return shift;
    }

    private static async Task<TimeClockShift> AddClosedShiftAsync(AppFactory app, TestStaff person, DateTime clockInUtc, DateTime clockOutUtc)
    {
        var shift = TimeClockShift.AddByHand(person.UserId, person.FullName, clockInUtc, clockOutUtc, "tests", "test data", DateTime.UtcNow);
        using var scope = app.Services.CreateScope();
        Assert.Equal(ShiftChangeOutcome.Saved, await scope.ServiceProvider.GetRequiredService<ITimeClockRepository>().AddClosedShiftAsync(shift));
        return shift;
    }

    private static async Task<ShiftChangeOutcome> TryAddShiftAsync(AppFactory app, TestStaff person, DateTime clockInUtc, DateTime clockOutUtc)
    {
        var shift = TimeClockShift.AddByHand(person.UserId, person.FullName, clockInUtc, clockOutUtc, "tests", "test data", DateTime.UtcNow);
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITimeClockRepository>().AddClosedShiftAsync(shift);
    }

    private static async Task<ShiftChangeOutcome> ChangeTimesAsync(AppFactory app, TimeClockShift shift, DateTime clockInUtc, DateTime clockOutUtc)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITimeClockRepository>().ChangeTimesAsync(new ShiftTimesChange(
            shift.Id, shift.UserId, shift.Version, clockInUtc, clockOutUtc, ClockOutSource.Manager, "tests", "test data", DateTime.UtcNow));
    }

    private static async Task<IReadOnlyList<TimeClockShift>> ShiftsOfAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<ITimeClockRepository>().SearchAsync(new ShiftSearch { UserId = userId, PageSize = 100 })).Items;
    }

    private static async Task<IReadOnlyList<StaffAuditEntryDto>> AuditOfAsync(AppFactory app, string userId)
    {
        using var scope = app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStaffAuditLog>().ListAsync(userId, 50);
    }

    private static async Task ExecuteAsync(AppFactory app, string sql)
    {
        await using var connection = await app.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await connection.ExecuteAsync(sql);
    }

    /// <summary>The app's clock moved to 4:00 AM business time today (and running on from there).</summary>
    private sealed class EarlyMorningClock(IDateTimeProvider real) : IDateTimeProvider
    {
        private readonly TimeSpan _offset = real.ToUtc(real.BusinessNow.Date.AddHours(4)) - real.UtcNow;

        public DateTime UtcNow => real.UtcNow + _offset;
        public DateTime BusinessNow => real.ToBusinessTime(UtcNow);
        public TimeZoneInfo BusinessTimeZone => real.BusinessTimeZone;
        public DateTime ToBusinessTime(DateTime utc) => real.ToBusinessTime(utc);
        public DateTime ToUtc(DateTime businessLocal) => real.ToUtc(businessLocal);
    }

    /// <summary>Collects what the app logs at Error level or above.</summary>
    private sealed class ErrorLog : ILoggerProvider, ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                Messages.Enqueue(formatter(state, exception));
            }
        }

        public void Dispose()
        {
        }
    }

    [GeneratedRegex("name=\"Version\" value=\"(\\d+)\"")]
    private static partial Regex VersionField();

    [GeneratedRegex("name=\"__revision\" type=\"hidden\" value=\"(\\d+)\"")]
    private static partial Regex RevisionField();

    [GeneratedRegex("<form[^>]*>(?:(?!</form>).)*I'm finishing now.*?</form>", RegexOptions.Singleline)]
    private static partial Regex FinishingNowForm();
}
