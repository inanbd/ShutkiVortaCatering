using System.Net;
using System.Text;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Features.Wholesale;

/// <summary>New restaurant request: alert the admins and acknowledge it to the restaurant.</summary>
internal sealed class StandingOrderSubmittedEmailHandler(
    IStandingOrderRepository standingOrders,
    StandingOrderScheduler scheduler,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    IOptions<EmailOptions> emailOptions,
    IOptions<WholesaleOptions> wholesale,
    ILogger<StandingOrderSubmittedEmailHandler> logger) : INotificationHandler<StandingOrderSubmittedNotification>
{
    public async Task Handle(StandingOrderSubmittedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var so = await standingOrders.GetByIdAsync(notification.StandingOrderId, cancellationToken);
            if (so is null)
            {
                return;
            }

            var dto = scheduler.ToDetails(so, []);
            var model = StandingOrderEmailModel.Build(dto, urls, wholesale.Value);

            var admins = emailOptions.Value.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (admins.Count > 0)
            {
                var adminEmail = renderer.Render(
                    EmailTemplates.AdminNewStandingOrder,
                    $"🍽️ Restaurant order request from {dto.BusinessName} · {dto.DaysText} · {model["TotalPerDelivery"]}/delivery",
                    model);
                foreach (var admin in admins)
                {
                    await email.QueueAsync(EmailMessage.Create(admin, adminEmail, replyTo: dto.Email), cancellationToken);
                }
            }
            else
            {
                logger.LogWarning("No admin recipients configured (Email:AdminRecipients); standing order {Reference} was not emailed to admins", dto.Reference);
            }

            var reply = renderer.Render(
                EmailTemplates.StandingOrderReceived,
                $"ধন্যবাদ! We received your restaurant order request {dto.Reference}",
                model);
            await email.QueueAsync(EmailMessage.Create(dto.Email, reply, emailOptions.Value.ReplyToAddress), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue emails for standing order {Id}", notification.StandingOrderId);
        }
    }
}

/// <summary>Tells the restaurant about approval, pause, cancellation…; tells admins when a restaurant changes its own order.</summary>
internal sealed class StandingOrderStatusChangedEmailHandler(
    IStandingOrderRepository standingOrders,
    StandingOrderScheduler scheduler,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    IOptions<EmailOptions> emailOptions,
    IOptions<WholesaleOptions> wholesale,
    ILogger<StandingOrderStatusChangedEmailHandler> logger) :
    INotificationHandler<StandingOrderStatusChangedNotification>,
    INotificationHandler<StandingOrderDateChangedNotification>
{
    public async Task Handle(StandingOrderStatusChangedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var so = await standingOrders.GetByIdAsync(notification.StandingOrderId, cancellationToken);
            if (so is null)
            {
                return;
            }

            var dto = scheduler.ToDetails(so, await scheduler.GetUpcomingAsync(so, 14, cancellationToken));
            var model = StandingOrderEmailModel.Build(dto, urls, wholesale.Value);
            model["StatusNote"] = notification.Note;
            model["IsApproved"] = notification.Action == StandingOrderAction.Approve;
            model["IsStopped"] = notification.Action is StandingOrderAction.Cancel or StandingOrderAction.Decline;
            model["Headline"] = notification.Action switch
            {
                StandingOrderAction.Approve => "Your standing order is approved",
                StandingOrderAction.Decline => "We can't take this standing order right now",
                StandingOrderAction.Pause => "Your standing order is paused",
                StandingOrderAction.Resume => "Your deliveries are back on",
                _ => "Your standing order has been cancelled",
            };
            model["StatusMessage"] = notification.Action switch
            {
                StandingOrderAction.Approve => $"Fresh vortas are on the schedule. Your first delivery is {model["NextDelivery"] ?? "coming up soon"}.",
                StandingOrderAction.Decline => "Thank you for thinking of us. Please call us — we would love to find something that works for your kitchen.",
                StandingOrderAction.Pause => "No new deliveries will be scheduled until the order is resumed.",
                StandingOrderAction.Resume => $"Deliveries continue on {dto.DaysText}. Next delivery: {model["NextDelivery"] ?? "to be scheduled"}.",
                _ => "No further deliveries will be made. Thank you for serving our vortas.",
            };

            if (notification.NotifyRestaurant && emailOptions.Value.SendCustomerStatusUpdates)
            {
                var subject = notification.Action switch
                {
                    StandingOrderAction.Approve => $"✅ Approved: standing order {dto.Reference} for {dto.BusinessName}",
                    StandingOrderAction.Decline => $"Your restaurant order request {dto.Reference}",
                    StandingOrderAction.Pause => $"Standing order {dto.Reference} is paused",
                    StandingOrderAction.Resume => $"Standing order {dto.Reference} has resumed",
                    _ => $"Standing order {dto.Reference} has been cancelled",
                };
                var rendered = renderer.Render(EmailTemplates.StandingOrderStatusUpdate, subject, model);
                await email.QueueAsync(EmailMessage.Create(dto.Email, rendered, emailOptions.Value.ReplyToAddress), cancellationToken);
            }

            if (notification.ByRestaurant)
            {
                var verb = notification.Action switch
                {
                    StandingOrderAction.Pause => "paused",
                    StandingOrderAction.Resume => "resumed",
                    _ => "cancelled",
                };
                await NotifyAdminsAsync(dto, model, $"{dto.BusinessName} {verb} their standing order", notification.Note, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue status emails for standing order {Id}", notification.StandingOrderId);
        }
    }

    public async Task Handle(StandingOrderDateChangedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var so = await standingOrders.GetByIdAsync(notification.StandingOrderId, cancellationToken);
            if (so is null)
            {
                return;
            }

            var dto = scheduler.ToDetails(so, await scheduler.GetUpcomingAsync(so, 14, cancellationToken));
            var model = StandingOrderEmailModel.Build(dto, urls, wholesale.Value);
            var date = notification.Date.ToDateTime(so.PreferredTime);
            var what = notification.Skipped
                ? $"{dto.BusinessName} skipped the delivery on {Format.Date(date)}"
                : $"{dto.BusinessName} restored the delivery on {Format.Date(date)}";
            await NotifyAdminsAsync(dto, model, what, null, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue date-change emails for standing order {Id}", notification.StandingOrderId);
        }
    }

    private async Task NotifyAdminsAsync(StandingOrderDetailsDto dto, Dictionary<string, object?> model, string what, string? note, CancellationToken cancellationToken)
    {
        var admins = emailOptions.Value.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        if (admins.Count == 0)
        {
            return;
        }

        model["ChangeDescription"] = what;
        model["ChangeNote"] = note;
        var rendered = renderer.Render(EmailTemplates.AdminStandingOrderChanged, $"🔔 {what} ({dto.Reference})", model);
        foreach (var admin in admins)
        {
            await email.QueueAsync(EmailMessage.Create(admin, rendered, replyTo: dto.Email), cancellationToken);
        }
    }
}

/// <summary>Template values shared by all standing-order emails.</summary>
internal static class StandingOrderEmailModel
{
    public static Dictionary<string, object?> Build(StandingOrderDetailsDto so, IAppUrls urls, WholesaleOptions wholesale)
    {
        var firstName = so.ContactName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? so.ContactName;
        var next = so.NextDelivery;

        return new Dictionary<string, object?>
        {
            ["Reference"] = so.Reference,
            ["RestaurantName"] = so.BusinessName,
            ["ContactName"] = so.ContactName,
            ["ContactFirstName"] = firstName,
            ["CustomerEmail"] = so.Email,
            ["CustomerPhone"] = so.Phone,
            ["TaxPermitNumber"] = so.TaxPermitNumber,
            ["TaxExempt"] = so.TaxExempt,
            ["FulfillmentName"] = so.FulfillmentName,
            ["IsDelivery"] = so.IsDelivery,
            ["DeliveryAddress"] = so.DeliveryAddress,
            ["DaysText"] = so.DaysText,
            ["PreferredTime"] = Format.Time(so.PreferredTime),
            ["StartDate"] = Format.Date(so.StartDate.ToDateTime(TimeOnly.MinValue)),
            ["EndDate"] = so.EndDate is { } end ? Format.Date(end.ToDateTime(TimeOnly.MinValue)) : null,
            ["Notes"] = so.Notes,
            ["StatusName"] = so.StatusName,
            ["ItemsTable"] = new RawHtml(BuildItemsTable(so)),
            ["SubtotalPerDelivery"] = Format.Currency(so.EstimatePerDelivery.Subtotal),
            ["DeliveryFee"] = Format.Currency(so.EstimatePerDelivery.DeliveryFee),
            ["HasDeliveryFee"] = so.IsDelivery,
            ["TaxPerDelivery"] = Format.Currency(so.EstimatePerDelivery.Tax),
            ["TotalPerDelivery"] = Format.Currency(so.EstimatePerDelivery.Total),
            ["DeliveriesPerWeek"] = so.DeliveriesPerWeek.ToString(Format.Culture),
            ["EstimatedWeeklyTotal"] = Format.Currency(so.EstimatedWeeklyTotal),
            ["NextDelivery"] = next is null ? null : Format.DateTime(next.ScheduledFor),
            ["AdminUrl"] = urls.AdminStandingOrder(so.Id),
            ["AccountUrl"] = urls.CustomerStandingOrder(so.Reference),
            ["RestaurantsUrl"] = urls.Restaurants(),
            ["PaymentTerms"] = wholesale.PaymentTerms,
            ["ChangeCutoffHours"] = wholesale.ChangeCutoffHours.ToString(Format.Culture),
        };
    }

    private static string BuildItemsTable(StandingOrderDetailsDto so)
    {
        const string th = "padding:10px 8px;border-bottom:2px solid #c8932b;font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#7a1f2b;";
        const string td = "padding:12px 8px;border-bottom:1px dashed #e2cfa6;";
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#3b2416;\">");
        sb.Append("<tr>")
          .Append($"<th align=\"left\" style=\"{th}\">Vorta</th>")
          .Append($"<th align=\"center\" style=\"{th}\">Each delivery</th>")
          .Append($"<th align=\"right\" style=\"{th}\">Amount</th>")
          .Append("</tr>");

        foreach (var line in so.Lines)
        {
            sb.Append("<tr>")
              .Append($"<td style=\"{td}\"><strong>").Append(WebUtility.HtmlEncode(line.ItemName)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(line.ItemBengaliName))
            {
                sb.Append("<br><span style=\"color:#8a6d4b;font-size:13px;\">").Append(WebUtility.HtmlEncode(line.ItemBengaliName)).Append("</span>");
            }

            sb.Append("<br><span style=\"color:#8a6d4b;font-size:12px;\">")
              .Append(WebUtility.HtmlEncode(Format.Currency(line.UnitPrice))).Append(" / ").Append(WebUtility.HtmlEncode(line.Unit)).Append(" wholesale")
              .Append("</span></td>")
              .Append($"<td align=\"center\" style=\"{td}white-space:nowrap;\">").Append(WebUtility.HtmlEncode(line.QuantityText)).Append("</td>")
              .Append($"<td align=\"right\" style=\"{td}white-space:nowrap;\">").Append(WebUtility.HtmlEncode(Format.Currency(line.LineTotal))).Append("</td>")
              .Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
