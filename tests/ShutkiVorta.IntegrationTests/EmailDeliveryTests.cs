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

    private AppFactory CreateApp(string password) => new("Sqlite", new Dictionary<string, string?>
    {
        ["Email:DeliveryMethod"] = "Auto",
        ["Email:FromAddress"] = "orders@shutki.test",
        ["Email:Smtp:Host"] = "127.0.0.1",
        ["Email:Smtp:Port"] = _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Email:Smtp:Security"] = "None",
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
}
