using Microsoft.Extensions.DependencyInjection;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Features.Emails;
using ShutkiVorta.IntegrationTests.Infrastructure;

namespace ShutkiVorta.IntegrationTests;

/// <summary>
/// Sends real SMTP traffic (MailKit) to an in-process fake server, the way a production mail server is used:
/// only the Smtp section is filled in and DeliveryMethod stays "Auto".
/// </summary>
public sealed class EmailDeliveryTests : IAsyncLifetime
{
    private readonly FakeSmtpServer _smtp = new("orders@shutki.test", "app-password");
    private AppFactory _app = null!;

    public Task InitializeAsync()
    {
        _app = CreateApp(_smtp.Password);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
        await _smtp.DisposeAsync();
    }

    private AppFactory CreateApp(string password, string host = "127.0.0.1", string security = "None") => new("Sqlite", new Dictionary<string, string?>
    {
        ["Email:DeliveryMethod"] = "Auto",
        ["Email:FromAddress"] = "orders@shutki.test",
        ["Email:Smtp:Host"] = host,
        ["Email:Smtp:Port"] = _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Smtp:Security"] = security,
        ["Email:Smtp:UserName"] = _smtp.UserName,
        ["Email:Smtp:Password"] = password,
        ["Email:Smtp:TimeoutSeconds"] = "10",
    });

    private static EmailMessage Message(string to, string subject = "Test ✓ শুঁটকি ভর্তা") =>
        EmailMessage.Create(to, new RenderedEmail(subject, "<p>Hello from the <b>kitchen</b></p>", "Hello from the kitchen"));

    [Fact]
    public async Task AutoMode_WithSmtpSettings_SendsThroughTheServer()
    {
        using var scope = _app.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();

        var result = await transport.SendAsync(Message("owner@shutki.test"));

        Assert.True(result.ActuallySent);
        Assert.Equal("Smtp", result.Method);
        Assert.Contains("queued as FAKE", result.Detail);
        var received = await _smtp.WaitForEmailAsync(e => e.Recipients.Contains("owner@shutki.test"), TimeSpan.FromSeconds(5));
        Assert.Equal("orders@shutki.test", received.AuthenticatedAs);
        Assert.Equal("orders@shutki.test", received.From);
        Assert.Matches("(?im)^Subject: Test =\\?utf-8\\?b\\?", received.Data); // Bengali subject is RFC 2047-encoded
        Assert.Contains("text/html", received.Data);
    }

    [Fact]
    public async Task ConnectionTest_ReportsEachStep()
    {
        using var scope = _app.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IEmailTransport>().TestConnectionAsync();

        Assert.True(result.Succeeded, result.Summary);
        Assert.Contains(result.Steps, s => s.Name == "Connect" && s.Succeeded && s.Detail.Contains("NOT encrypted"));
        Assert.Contains(result.Steps, s => s.Name == "Sign in" && s.Succeeded);
    }

    [Fact]
    public async Task QueuedEmails_AreDeliveredByTheBackgroundDispatcher_AndLogged()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEmailService>().QueueAsync(Message("kitchen@shutki.test", "New order SV-TEST"));

        await _smtp.WaitForEmailAsync(e => e.Recipients.Contains("kitchen@shutki.test"), TimeSpan.FromSeconds(15));

        var log = scope.ServiceProvider.GetRequiredService<IEmailLog>();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var entries = await log.ListAsync(EmailStatus.Sent, 1, 50);
            if (entries.Items.Any(e => e.Subject == "New order SV-TEST"))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("The delivered email was not marked as Sent in the email log.");
    }

    [Fact]
    public async Task WrongPassword_FailsWithAPlainEnglishHint()
    {
        await using var app = CreateApp("not-the-password");
        using var scope = app.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => transport.SendAsync(Message("owner@shutki.test")));

        Assert.NotNull(ex.Hint);
        Assert.Contains("password", ex.Hint, StringComparison.OrdinalIgnoreCase);
        var test = await transport.TestConnectionAsync();
        Assert.False(test.Succeeded);
        Assert.Contains(test.Steps, s => s.Name == "Sign in" && !s.Succeeded);
    }

    [Fact]
    public async Task SensitiveEmails_AreDeliveredInFull_ButRemovedFromTheLog()
    {
        using var scope = _app.Services.CreateScope();
        var message = Message("reset.user@shutki.test", "Reset your password") with { Sensitive = true };
        await scope.ServiceProvider.GetRequiredService<IEmailService>().QueueAsync(message);

        var received = await _smtp.WaitForEmailAsync(e => e.Recipients.Contains("reset.user@shutki.test"), TimeSpan.FromSeconds(15));
        Assert.Contains("kitchen", received.Data); // the customer gets the real content

        var log = scope.ServiceProvider.GetRequiredService<IEmailLog>();
        EmailLogEntryDto? entry = null;
        for (var attempt = 0; attempt < 50 && entry?.Status != EmailStatus.Sent; attempt++)
        {
            await Task.Delay(100);
            var id = (await log.ListAsync(null, 1, 50)).Items.First(e => e.ToAddresses == "reset.user@shutki.test").Id;
            entry = await log.GetAsync(id);
        }

        Assert.NotNull(entry);
        Assert.Equal(EmailStatus.Sent, entry.Status);
        Assert.True(entry.IsSensitive);
        Assert.Null(entry.HtmlBody);
        Assert.False(await log.RetryAsync(entry.Id));
    }

    [Fact]
    public async Task AutoSecurity_NeverSignsInUnencrypted_ToAnotherMachine()
    {
        // 127.0.0.2 stands in for a remote server that (maliciously or not) does not offer STARTTLS.
        await using var app = CreateApp(_smtp.Password, host: "127.0.0.2", security: "Auto");
        using var scope = app.Services.CreateScope();
        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();

        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => transport.SendAsync(Message("owner@shutki.test", "Must not be sent")));

        Assert.Contains("STARTTLS", ex.Message + ex.Hint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_smtp.Received, e => e.Data.Contains("Must not be sent"));

        // A mail server on this machine may still be used without encryption.
        await using var local = CreateApp(_smtp.Password, host: "127.0.0.1", security: "Auto");
        using var localScope = local.Services.CreateScope();
        Assert.True((await localScope.ServiceProvider.GetRequiredService<IEmailTransport>().SendAsync(Message("owner@shutki.test"))).ActuallySent);
    }
}
