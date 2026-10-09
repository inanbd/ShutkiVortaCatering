using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Emails;

namespace ShutkiVorta.Application.Features.Settings;

/// <summary>Describes the configured database so admins can see which provider is active.</summary>
public interface IDatabaseInfo
{
    string ProviderName { get; }
    string DataSource { get; }
}

public sealed record GetSettingsOverviewQuery : IRequest<SettingsOverviewDto>, IRequireAdmin;

public sealed record SettingsOverviewDto(
    BusinessOptions Business,
    OrderingOptions Ordering,
    WholesaleOptions Wholesale,
    SiteOptions Site,
    EmailSettingsView Email,
    EmailConfigurationReport EmailReport,
    EmailStatusCounts EmailLast7Days,
    string DatabaseProvider,
    string DatabaseSource);

/// <summary>Email settings without secrets.</summary>
public sealed record EmailSettingsView(
    bool Enabled,
    string DeliveryMethod,
    string EffectiveDeliveryMethod,
    string FromName,
    string FromAddress,
    string? ReplyToAddress,
    IReadOnlyList<string> AdminRecipients,
    string SmtpHost,
    int SmtpPort,
    string SmtpSecurity,
    bool HasSmtpCredentials,
    string? SmtpUserName,
    bool AcceptInvalidCertificates,
    bool CheckCertificateRevocation,
    string PickupDirectory,
    bool SendCustomerStatusUpdates);

/// <summary>Sends a test email immediately and reports exactly what happened (sent, saved to folder, or the error).</summary>
public sealed record SendTestEmailCommand(string To) : IRequest<TestEmailOutcome>, IRequireAdmin;

public sealed record TestEmailOutcome(bool Succeeded, bool ActuallySent, string Message, string? Hint);

internal sealed class SettingsHandlers(
    IOptions<BusinessOptions> business,
    IOptions<OrderingOptions> ordering,
    IOptions<WholesaleOptions> wholesale,
    IOptions<SiteOptions> site,
    IOptionsMonitor<EmailOptions> email,
    IDatabaseInfo database,
    IEmailDiagnostics diagnostics,
    IEmailLog emailLog,
    IEmailTemplateRenderer renderer,
    IEmailTransport transport,
    IDateTimeProvider clock) :
    IRequestHandler<GetSettingsOverviewQuery, SettingsOverviewDto>,
    IRequestHandler<SendTestEmailCommand, TestEmailOutcome>
{
    public async Task<SettingsOverviewDto> Handle(GetSettingsOverviewQuery request, CancellationToken cancellationToken)
    {
        var e = email.CurrentValue;
        var view = new EmailSettingsView(
            e.Enabled,
            e.DeliveryMethod,
            e.ResolveDeliveryMethod(),
            e.FromName,
            e.FromAddress,
            e.ReplyToAddress,
            e.AdminRecipients,
            e.Smtp.Host,
            e.Smtp.Port,
            e.Smtp.Security,
            !string.IsNullOrWhiteSpace(e.Smtp.UserName),
            e.Smtp.UserName,
            e.Smtp.AcceptInvalidCertificates,
            e.Smtp.CheckCertificateRevocation,
            e.PickupDirectory,
            e.SendCustomerStatusUpdates);

        var counts = await emailLog.CountSinceAsync(clock.UtcNow.AddDays(-7), cancellationToken);

        return new SettingsOverviewDto(
            business.Value,
            ordering.Value,
            wholesale.Value,
            site.Value,
            view,
            diagnostics.GetReport(),
            counts,
            database.ProviderName,
            database.DataSource);
    }

    public async Task<TestEmailOutcome> Handle(SendTestEmailCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@'))
        {
            return new TestEmailOutcome(false, false, "Please enter a valid email address.", null);
        }

        var report = diagnostics.GetReport();
        var rendered = renderer.Render(EmailTemplates.TestEmail, "Test email from your catering website", new Dictionary<string, object?>
        {
            ["SentAt"] = Common.Formatting.Format.DateTime(clock.BusinessNow),
            ["DeliveryMethod"] = report.EffectiveMethod,
        });
        var message = EmailMessage.Create(request.To.Trim(), rendered);

        if (!report.Enabled)
        {
            await emailLog.RecordAsync(message, EmailStatus.Disabled, null, null, "Email:Enabled is false", cancellationToken);
            return new TestEmailOutcome(false, false, "Email is switched off, so nothing was sent.", "Tick \"Send emails\" in Admin → Settings → Email.");
        }

        try
        {
            var result = await transport.SendAsync(message, cancellationToken);
            await emailLog.RecordAsync(
                message,
                result.ActuallySent ? EmailStatus.Sent : EmailStatus.SavedToFolder,
                result.Method,
                result.Detail,
                null,
                cancellationToken);

            return result.ActuallySent
                ? new TestEmailOutcome(true, true, $"Test email accepted by the mail server for {request.To.Trim()}. Server reply: {result.Detail}", "If it does not arrive within a few minutes, check the spam folder and your mail provider's sending logs.")
                : new TestEmailOutcome(true, false, $"The test email was NOT sent: emails are being saved to a folder ({result.Detail}).", report.Warnings.FirstOrDefault());
        }
        catch (Exception ex)
        {
            var hint = (ex as EmailDeliveryException)?.Hint;
            await emailLog.RecordAsync(message, EmailStatus.Failed, report.EffectiveMethod, null, ex.Message, cancellationToken);
            return new TestEmailOutcome(false, false, $"Sending failed: {ex.Message}", hint);
        }
    }
}
