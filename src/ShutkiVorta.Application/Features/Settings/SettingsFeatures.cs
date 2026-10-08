using MediatR;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Models;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Application.Common.Security;

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
    SiteOptions Site,
    EmailSettingsView Email,
    string DatabaseProvider,
    string DatabaseSource);

/// <summary>Email settings without secrets.</summary>
public sealed record EmailSettingsView(
    bool Enabled,
    string DeliveryMethod,
    string FromName,
    string FromAddress,
    string? ReplyToAddress,
    IReadOnlyList<string> AdminRecipients,
    string SmtpHost,
    int SmtpPort,
    string SmtpSecurity,
    bool HasSmtpCredentials,
    string PickupDirectory,
    bool SendCustomerStatusUpdates);

public sealed record SendTestEmailCommand(string To) : IRequest<Result>, IRequireAdmin;

internal sealed class SettingsHandlers(
    IOptions<BusinessOptions> business,
    IOptions<OrderingOptions> ordering,
    IOptions<SiteOptions> site,
    IOptions<EmailOptions> email,
    IDatabaseInfo database,
    IEmailTemplateRenderer renderer,
    IEmailTransport transport,
    IDateTimeProvider clock) :
    IRequestHandler<GetSettingsOverviewQuery, SettingsOverviewDto>,
    IRequestHandler<SendTestEmailCommand, Result>
{
    public Task<SettingsOverviewDto> Handle(GetSettingsOverviewQuery request, CancellationToken cancellationToken)
    {
        var e = email.Value;
        var view = new EmailSettingsView(
            e.Enabled,
            e.DeliveryMethod,
            e.FromName,
            e.FromAddress,
            e.ReplyToAddress,
            e.AdminRecipients,
            e.Smtp.Host,
            e.Smtp.Port,
            e.Smtp.Security,
            !string.IsNullOrWhiteSpace(e.Smtp.UserName),
            e.PickupDirectory,
            e.SendCustomerStatusUpdates);

        return Task.FromResult(new SettingsOverviewDto(business.Value, ordering.Value, site.Value, view, database.ProviderName, database.DataSource));
    }

    public async Task<Result> Handle(SendTestEmailCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@'))
        {
            return Result.Failure("Please enter a valid email address.");
        }

        var rendered = renderer.Render(EmailTemplates.TestEmail, "Test email from your catering website", new Dictionary<string, object?>
        {
            ["SentAt"] = Common.Formatting.Format.DateTime(clock.BusinessNow),
            ["DeliveryMethod"] = email.Value.DeliveryMethod,
        });

        try
        {
            await transport.SendAsync(EmailMessage.Create(request.To.Trim(), rendered), cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Sending failed: {ex.Message}");
        }
    }
}
