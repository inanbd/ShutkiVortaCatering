using MediatR;
using NSubstitute;
using ShutkiVorta.Application.Common.Settings;
using ShutkiVorta.Application.Features.Settings;
using ShutkiVorta.UnitTests.TestDoubles;
using static ShutkiVorta.UnitTests.Application.Settings.SettingsTestHelpers;
using ValidationException = ShutkiVorta.Application.Common.Exceptions.ValidationException;

namespace ShutkiVorta.UnitTests.Application.Settings;

/// <summary>Reading and saving settings pages: form values in, canonical stored values out, errors keyed by setting key.</summary>
public sealed class ManagedSettingsHandlerTests
{
    private readonly InMemorySettingsStore _store = new();
    private readonly IPublisher _publisher = Substitute.For<IPublisher>();
    private readonly FakeClock _clock = new(TestData.Now);
    private readonly FakeDiagnostics _diagnostics = new();

    public ManagedSettingsHandlerTests()
    {
        foreach (var section in ManagedSettings.Sections)
        {
            _store.Seed(section.Name, SettingsFlattener.Flatten(section.CreateDefaults(), section.Name).Where(kv => !ManagedSettings.IsSecret(kv.Key)));
        }
    }

    private ManagedSettingsHandlers Handler() =>
        new(_store, _diagnostics, Validators, new FakeCurrentUser { UserId = "admin-1", Email = "owner@shutki.test", IsAdmin = true }, _clock, _publisher);

    private async Task<SettingsPageDto> PageAsync(string slug) => (await Handler().Handle(new GetSettingsPageQuery(slug), CancellationToken.None))!;

    /// <summary>The command a browser would post for the page as currently shown, with some fields changed.</summary>
    private async Task<UpdateSettingsPageCommand> FormAsync(
        string slug,
        Dictionary<string, string?>? values = null,
        Dictionary<string, IReadOnlyList<string>>? lists = null)
    {
        var page = await PageAsync(slug);
        var single = page.Fields
            .Where(f => f.Field.Kind is not (SettingKind.List or SettingKind.DaysOfWeek or SettingKind.Secret))
            .ToDictionary(f => f.Field.Key, f => f.Value, StringComparer.OrdinalIgnoreCase);
        var multi = page.Fields
            .Where(f => f.Field.Kind is SettingKind.List or SettingKind.DaysOfWeek)
            .ToDictionary(f => f.Field.Key, f => f.Values, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values ?? [])
        {
            single[key] = value;
        }

        foreach (var (key, value) in lists ?? [])
        {
            multi[key] = value;
        }

        return new UpdateSettingsPageCommand { Slug = slug, Revision = page.Revision, Values = single, Lists = multi };
    }

    private async Task<ValidationException> InvalidAsync(UpdateSettingsPageCommand command)
    {
        var before = _store.Saves;
        _publisher.ClearReceivedCalls();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => Handler().Handle(command, CancellationToken.None));
        Assert.Equal(before, _store.Saves); // nothing was saved
        await _publisher.DidNotReceive().Publish(Arg.Any<SettingsChangedNotification>(), Arg.Any<CancellationToken>());
        return ex;
    }

    // ---- Reading -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pages_ListEveryCatalogPage_WithWhenAndByWhomItLastChanged()
    {
        var pages = await Handler().Handle(new GetSettingsPagesQuery(), CancellationToken.None);

        Assert.Equal(SettingsCatalog.Pages.Select(p => p.Slug), pages.Select(p => p.Page.Slug));
        Assert.All(pages, p => Assert.Equal("Imported from configuration", p.LastChangedBy));
        Assert.All(pages, p => Assert.Equal(TestData.Now.AddDays(-1), p.LastChangedAtLocal));
    }

    [Fact]
    public async Task Page_UnknownSlug_IsNull()
    {
        Assert.Null(await Handler().Handle(new GetSettingsPageQuery("nope"), CancellationToken.None));
    }

    [Fact]
    public async Task Page_ShowsEveryCatalogField_WithPercentagesListsAndSecretState()
    {
        _store.Seed("Ordering", [new("Ordering:BlackoutDates:0", "2027-03-20"), new("Ordering:BlackoutDates:10", "2027-12-31"), new("Ordering:BlackoutDates:2", "2027-06-16")]);
        _store.SetSecret("Email:Smtp:Password", SecretState.Saved);

        var ordering = await PageAsync("ORDERING");
        Assert.Equal("ordering", ordering.Page.Slug);
        Assert.Equal(1, ordering.Revision);
        Assert.Equal(SettingsCatalog.FindBySlug("ordering")!.Fields.Select(f => f.Key), ordering.Fields.Select(f => f.Field.Key));
        Assert.Equal("8.25", ordering.Fields.Single(f => f.Field.Key == "Ordering:TaxRate").Value);
        Assert.Equal("true", ordering.Fields.Single(f => f.Field.Key == "Ordering:AcceptingOrders").Value);
        Assert.Equal(["Monday"], ordering.Fields.Single(f => f.Field.Key == "Ordering:ClosedDays").Values);
        Assert.Equal(["2027-03-20", "2027-06-16", "2027-12-31"], ordering.Fields.Single(f => f.Field.Key == "Ordering:BlackoutDates").Values); // numeric order
        Assert.Null(ordering.Fields.Single(f => f.Field.Key == "Ordering:ClosedDays").Value);

        var email = await PageAsync("email");
        var password = email.Fields.Single(f => f.Field.Key == ManagedSettings.SmtpPasswordKey);
        Assert.Equal(SecretState.Saved, password.Secret);
        Assert.Null(password.Value);
        Assert.Empty(password.Values);
        Assert.Equal(SecretState.Empty, (await PageAsync("business")).Fields[0].Secret);

        var business = await PageAsync("business");
        var zone = business.Fields.Single(f => f.Field.Key == "Business:TimeZoneId");
        Assert.Same(SettingsCatalog.TimeZones, zone.Choices);
        Assert.Equal("$$", business.Fields.Single(f => f.Field.Key == "Business:PriceRange").Value);
    }

    // ---- Saving ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Save_UnchangedForm_StoresTheSameValues()
    {
        foreach (var page in SettingsCatalog.Pages)
        {
            var before = _store.Values(page.Section);
            await Handler().Handle(await FormAsync(page.Slug), CancellationToken.None);
            Assert.Equal(before.OrderBy(kv => kv.Key), _store.Values(page.Section).OrderBy(kv => kv.Key));
        }
    }

    [Fact]
    public async Task Save_StoresCanonicalValues_BumpsTheRevision_AndAnnouncesTheChange()
    {
        var command = await FormAsync(
            "ordering",
            new()
            {
                ["Ordering:TaxRate"] = "8.5",
                ["Ordering:DeliveryFee"] = "$12.50",
                ["Ordering:FreeDeliveryThreshold"] = "",
                ["Ordering:FirstSlot"] = "9:5",
                ["Ordering:AcceptingOrders"] = null, // unchecked box
                ["Ordering:PausedMessage"] = "  Back after Eid!  ",
            },
            new()
            {
                ["Ordering:ClosedDays"] = ["Sunday", "Monday", "monday"],
                ["Ordering:DeliveryZipPrefixes"] = [" 752 ", "", "760", "752"],
                ["Ordering:BlackoutDates"] = [],
            });

        var revision = await Handler().Handle(command, CancellationToken.None);

        Assert.Equal(2, revision);
        var saved = _store.LastSave!;
        Assert.Equal("Ordering", saved.Section);
        Assert.Equal(1, saved.ExpectedRevision);
        Assert.Equal("owner@shutki.test", saved.ChangedBy);
        Assert.Equal("0.085", saved.Values["Ordering:TaxRate"]);
        Assert.Equal("12.50", saved.Values["Ordering:DeliveryFee"]);
        Assert.Equal(string.Empty, saved.Values["Ordering:FreeDeliveryThreshold"]);
        Assert.Equal("09:05", saved.Values["Ordering:FirstSlot"]);
        Assert.Equal("false", saved.Values["Ordering:AcceptingOrders"]);
        Assert.Equal("Back after Eid!", saved.Values["Ordering:PausedMessage"]);
        Assert.Equal("Sunday", saved.Values["Ordering:ClosedDays:0"]);
        Assert.Equal("Monday", saved.Values["Ordering:ClosedDays:1"]);
        Assert.False(saved.Values.ContainsKey("Ordering:ClosedDays:2")); // duplicates removed
        Assert.Equal("752", saved.Values["Ordering:DeliveryZipPrefixes:0"]);
        Assert.Equal("760", saved.Values["Ordering:DeliveryZipPrefixes:1"]);
        Assert.False(saved.Values.ContainsKey("Ordering:DeliveryZipPrefixes:2"));
        Assert.DoesNotContain(saved.Values.Keys, k => k.StartsWith("Ordering:BlackoutDates", StringComparison.OrdinalIgnoreCase));
        Assert.All(saved.Values.Keys, k => Assert.StartsWith("Ordering:", k, StringComparison.Ordinal));
        Assert.Empty(saved.Secrets);

        await _publisher.Received(1).Publish(Arg.Is<SettingsChangedNotification>(n => n.Section == "Ordering"), Arg.Any<CancellationToken>());
        Assert.Equal("8.5", (await PageAsync("ordering")).Fields.Single(f => f.Field.Key == "Ordering:TaxRate").Value);
    }

    [Fact]
    public async Task Save_NeverWritesSecretsFromTheValues()
    {
        var command = await FormAsync("email", new() { [ManagedSettings.SmtpPasswordKey] = "typed-into-the-wrong-dictionary" });

        await Handler().Handle(command, CancellationToken.None);

        Assert.False(_store.LastSave!.Values.ContainsKey(ManagedSettings.SmtpPasswordKey));
        Assert.Empty(_store.LastSave.Secrets);
    }

    [Fact]
    public async Task Secrets_NewValueIsSaved_BlankKeeps_ClearRemoves()
    {
        var form = await FormAsync("email");

        await Handler().Handle(form with { NewSecrets = new Dictionary<string, string?> { [ManagedSettings.SmtpPasswordKey] = "app-password" } }, CancellationToken.None);
        Assert.Equal(new SecretChange(false, "app-password"), _store.LastSave!.Secrets[ManagedSettings.SmtpPasswordKey]);

        form = await FormAsync("email");
        await Handler().Handle(form with { NewSecrets = new Dictionary<string, string?> { [ManagedSettings.SmtpPasswordKey] = "" } }, CancellationToken.None);
        Assert.Empty(_store.LastSave.Secrets); // blank = keep the stored password

        form = await FormAsync("email");
        await Handler().Handle(form with
        {
            NewSecrets = new Dictionary<string, string?> { [ManagedSettings.SmtpPasswordKey] = "ignored" },
            ClearSecrets = ["email:smtp:password"],
        }, CancellationToken.None);
        Assert.Equal(new SecretChange(true, null), _store.LastSave.Secrets[ManagedSettings.SmtpPasswordKey]);
    }

    [Fact]
    public async Task Save_UnknownPage_IsAValidationError()
    {
        var ex = await InvalidAsync(new UpdateSettingsPageCommand { Slug = "nope" });
        Assert.Equal("Unknown settings page.", Assert.Single(ex.Errors[string.Empty]));
    }

    // ---- Validation errors are keyed by the setting key -----------------------------------------------------------

    [Fact]
    public async Task Errors_FromParsing_AreKeyedBySettingKey()
    {
        var ex = await InvalidAsync(await FormAsync("ordering", new()
        {
            ["Ordering:MaxDaysInAdvance"] = "thirty",
            ["Ordering:MinimumLeadTimeHours"] = "",
            ["Ordering:TaxRate"] = "30",
            ["Ordering:FirstSlot"] = "noon",
        }));

        Assert.Equal(["Please enter a whole number."], ex.Errors["Ordering:MaxDaysInAdvance"]);
        Assert.Equal(["This field is required."], ex.Errors["Ordering:MinimumLeadTimeHours"]);
        Assert.Equal(["Please enter a value between 0 and 25."], ex.Errors["Ordering:TaxRate"]);
        Assert.Equal(["Please enter a time such as 09:30 or 17:00."], ex.Errors["Ordering:FirstSlot"]);
    }

    [Fact]
    public async Task Errors_FromTheSectionValidator_AreKeyedBySettingKey()
    {
        var ordering = await InvalidAsync(await FormAsync(
            "ordering",
            new() { ["Ordering:FirstSlot"] = "18:00", ["Ordering:LastSlot"] = "09:00" },
            new() { ["Ordering:DeliveryZipPrefixes"] = ["752", "75A"], ["Ordering:BlackoutDates"] = ["next friday"] }));

        Assert.Equal(["The last time must be the same as or later than the first time."], ordering.Errors["Ordering:LastSlot"]);
        Assert.Equal(["'75A' is not a ZIP code or ZIP prefix (1–5 digits)."], ordering.Errors["Ordering:DeliveryZipPrefixes"]);
        Assert.Equal(["'next friday' is not a date. Use yyyy-mm-dd, e.g. 2027-03-20."], ordering.Errors["Ordering:BlackoutDates"]);
        Assert.False(ordering.Errors.ContainsKey("Ordering:FirstSlot"));

        var email = await InvalidAsync(await FormAsync(
            "email",
            new() { ["Email:Smtp:Host"] = "https://smtp.gmail.com" },
            new() { ["Email:AdminRecipients"] = ["owner@shutki.test", "not-an-email"] }));

        Assert.Equal(["Enter only the server name, e.g. smtp.gmail.com (no https:// and no port)."], email.Errors["Email:Smtp:Host"]);
        Assert.Equal(["'not-an-email' is not an email address."], email.Errors["Email:AdminRecipients"]);

        var business = await InvalidAsync(await FormAsync("business", new() { ["Business:TimeZoneId"] = "Mars/Olympus_Mons", ["Business:Phone"] = "" }));
        Assert.Equal(["Please choose a time zone from the list."], business.Errors["Business:TimeZoneId"]);
        Assert.Contains("Business:Phone", business.Errors.Keys);

        var site = await InvalidAsync(await FormAsync("website", new() { ["Site:BaseUrl"] = "https://www.shutki.test/menu" }));
        Assert.Equal(["Enter just your secure web address, e.g. https://www.yourdomain.com (https, no page path)."], site.Errors["Site:BaseUrl"]);

        var days = await InvalidAsync(await FormAsync("ordering", lists: new() { ["Ordering:ClosedDays"] = [.. Enum.GetNames<DayOfWeek>()] }));
        Assert.Equal(["The kitchen must be open on at least one day."], days.Errors["Ordering:ClosedDays"]);

        // Every key names a field of the page that was posted, so the UI can show each error under its field.
        var keys = SettingsCatalog.FindBySlug("ordering")!.Fields.Select(f => f.Key).ToHashSet();
        Assert.All(ordering.Errors.Keys, k => Assert.Contains(k, keys));
    }

    [Fact]
    public async Task UnknownDayNames_AreNeverStored()
    {
        // Only a tampered form can post one; the configuration binder skips list items it cannot convert.
        await Handler().Handle(await FormAsync("ordering", lists: new() { ["Ordering:ClosedDays"] = ["Funday", "sunday"] }), CancellationToken.None);

        var closed = _store.Values("Ordering").Where(kv => kv.Key.StartsWith("Ordering:ClosedDays:", StringComparison.Ordinal)).ToList();
        Assert.Equal([new KeyValuePair<string, string?>("Ordering:ClosedDays:0", "Sunday")], closed);
    }

    [Fact]
    public async Task ConcurrentEdit_IsAGeneralError()
    {
        var form = await FormAsync("business", new() { ["Business:Phone"] = "(469) 555-0101" });
        await Handler().Handle(form, CancellationToken.None); // revision 1 → 2

        var stale = await InvalidAsync(form with { Values = new Dictionary<string, string?>(form.Values) { ["Business:Phone"] = "(469) 555-0202" } });

        var message = Assert.Single(stale.Errors[string.Empty]);
        Assert.Contains("changed by someone else", message);
        Assert.Contains("reload the page", message);
        Assert.Equal("(469) 555-0101", _store.Values("Business")["Business:Phone"]);
    }

    // ---- Mail server password guard -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Email:Smtp:Host", "smtp.attacker.test")]
    [InlineData("Email:Smtp:Port", "2525")]
    [InlineData("Email:Smtp:UserName", "someone-else")]
    [InlineData("Email:Smtp:Security", "None")]
    [InlineData("Email:Smtp:AcceptInvalidCertificates", "true")]
    public async Task ChangingTheMailServer_WithASavedPassword_RequiresTypingItAgain(string key, string value)
    {
        _store.Seed("Email", [new("Email:Smtp:Host", "smtp.shutki.test"), new("Email:Smtp:UserName", "kitchen")]);
        _store.SetSecret(ManagedSettings.SmtpPasswordKey, SecretState.Saved);

        var ex = await InvalidAsync(await FormAsync("email", new() { [key] = value }));

        Assert.Contains("password", Assert.Single(ex.Errors[ManagedSettings.SmtpPasswordKey]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangingTheMailServer_IsSaved_WhenThePasswordIsTypedAgainOrRemoved()
    {
        _store.Seed("Email", [new("Email:Smtp:Host", "smtp.shutki.test"), new("Email:Smtp:UserName", "kitchen")]);
        _store.SetSecret(ManagedSettings.SmtpPasswordKey, SecretState.Saved);

        var form = await FormAsync("email", new() { ["Email:Smtp:Host"] = "smtp.new.test" });
        await Handler().Handle(form with { NewSecrets = new Dictionary<string, string?> { [ManagedSettings.SmtpPasswordKey] = "new-password" } }, CancellationToken.None);
        Assert.Equal("smtp.new.test", _store.Values("Email")["Email:Smtp:Host"]);

        form = await FormAsync("email", new() { ["Email:Smtp:Host"] = "smtp.other.test" });
        await Handler().Handle(form with { ClearSecrets = [ManagedSettings.SmtpPasswordKey] }, CancellationToken.None);
        Assert.Equal("smtp.other.test", _store.Values("Email")["Email:Smtp:Host"]);
    }

    [Fact]
    public async Task OtherEmailSettings_CanChange_WithoutTypingThePasswordAgain()
    {
        _store.Seed("Email", [new("Email:Smtp:Host", "smtp.shutki.test"), new("Email:Smtp:UserName", "kitchen")]);
        _store.SetSecret(ManagedSettings.SmtpPasswordKey, SecretState.Saved);

        await Handler().Handle(await FormAsync("email", new() { ["Email:FromName"] = "Shutki Kitchen", ["Email:Smtp:Host"] = " SMTP.shutki.test " }), CancellationToken.None);

        Assert.Equal("Shutki Kitchen", _store.Values("Email")["Email:FromName"]);
        Assert.Empty(_store.LastSave!.Secrets);
    }

    // ---- Review flag, history and notices ---------------------------------------------------------------------------

    [Fact]
    public async Task Page_ShowsTheReviewFlag_AndRecentChangesAsAdminsTypedThem()
    {
        _store.NeedsReview.Add("Ordering");
        _store.History.AddRange(
        [
            new SettingChange("Ordering:TaxRate", "0.0825", "0.0875", "owner@shutki.test", TestData.Now.AddHours(-2)),
            new SettingChange("Ordering:AcceptingOrders", "True", "False", "owner@shutki.test", TestData.Now.AddHours(-1)),
            new SettingChange("Ordering:ClosedDays", "Monday", "Monday, Tuesday", "owner@shutki.test", TestData.Now),
        ]);

        var page = await PageAsync("ordering");

        Assert.True(page.NeedsReview);
        Assert.Collection(
            page.History, // newest first
            c => Assert.Equal(("Monday", "Monday, Tuesday"), (c.OldValue, c.NewValue)),
            c => Assert.Equal(("On", "Off"), (c.OldValue, c.NewValue)),
            c => Assert.Equal(("Sales tax", "8.25%", "8.75%"), (c.Label, c.OldValue, c.NewValue)));
        Assert.All(page.History, c => Assert.Equal("owner@shutki.test", c.ChangedBy));

        var summaries = await Handler().Handle(new GetSettingsPagesQuery(), CancellationToken.None);
        Assert.Equal(["ordering"], summaries.Where(p => p.NeedsReview).Select(p => p.Page.Slug));
    }

    [Fact]
    public async Task Notices_ListPagesToReview_RejectedValues_AndIgnoredServerConfiguration()
    {
        _store.NeedsReview.Add("Email");
        _diagnostics.RejectedKeys = ["Ordering:TaxRate"];
        _diagnostics.IgnoredConfigurationKeys = ["Email:Smtp:Host"];

        var notices = await Handler().Handle(new GetSettingsNoticesQuery(), CancellationToken.None);

        Assert.True(notices.Any);
        Assert.Equal(["email"], notices.PagesNeedingReview.Select(p => p.Slug));
        Assert.Equal(["Ordering:TaxRate"], notices.RejectedKeys);
        Assert.Equal(["Email:Smtp:Host"], notices.IgnoredConfigurationKeys);
    }

    /// <summary>A store that keeps sections in memory and enforces the revision check like the database does.</summary>
    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, Dictionary<string, string?>> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _revisions = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SecretState> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public int Saves { get; private set; }

        public HashSet<string> NeedsReview { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<SettingChange> History { get; } = [];

        public SavedSection? LastSave { get; private set; }

        public void Seed(string section, IEnumerable<KeyValuePair<string, string?>> values)
        {
            if (!_values.TryGetValue(section, out var stored))
            {
                _values[section] = stored = new(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var (key, value) in values)
            {
                stored[key] = value;
            }

            _revisions.TryAdd(section, 1);
        }

        public void SetSecret(string key, SecretState state) => _secrets[key] = state;

        public Dictionary<string, string?> Values(string section) => new(_values[section], StringComparer.OrdinalIgnoreCase);

        public Task<StoredSection> GetSectionAsync(string section, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StoredSection(
                Values(section),
                _secrets.Where(s => s.Key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)).ToDictionary(s => s.Key, s => s.Value),
                _revisions[section],
                TestData.Now.AddDays(-1),
                "Imported from configuration")
            {
                NeedsReview = NeedsReview.Contains(section),
                History = [.. History.Where(h => h.Key.StartsWith(section + ":", StringComparison.OrdinalIgnoreCase)).OrderByDescending(h => h.ChangedAtUtc)],
            });

        public Task<int> SaveSectionAsync(
            string section,
            IReadOnlyDictionary<string, string?> values,
            IReadOnlyDictionary<string, SecretChange> secrets,
            int expectedRevision,
            string changedBy,
            CancellationToken cancellationToken = default)
        {
            if (_revisions[section] != expectedRevision)
            {
                throw new SettingsConflictException();
            }

            Saves++;
            LastSave = new SavedSection(section, new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase), secrets, expectedRevision, changedBy);
            _values[section] = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(++_revisions[section]);
        }
    }

    private sealed record SavedSection(
        string Section,
        Dictionary<string, string?> Values,
        IReadOnlyDictionary<string, SecretChange> Secrets,
        int ExpectedRevision,
        string ChangedBy);

    private sealed class FakeDiagnostics : ISettingsDiagnostics
    {
        public IReadOnlyList<string> RejectedKeys { get; set; } = [];

        public IReadOnlyList<string> IgnoredConfigurationKeys { get; set; } = [];
    }
}
