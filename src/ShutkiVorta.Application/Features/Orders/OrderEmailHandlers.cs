using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;
using ShutkiVorta.Domain.Orders;

namespace ShutkiVorta.Application.Features.Orders;

/// <summary>Emails the kitchen/admins about a new order and sends the customer their confirmation.</summary>
internal sealed class OrderPlacedEmailHandler(
    IOrderRepository orders,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    IDateTimeProvider clock,
    IOptions<EmailOptions> emailOptions,
    IOptions<BusinessOptions> business,
    IOptions<OrderingOptions> ordering,
    ILogger<OrderPlacedEmailHandler> logger) : INotificationHandler<OrderPlacedNotification>
{
    public async Task Handle(OrderPlacedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var order = await orders.GetByNumberAsync(notification.OrderNumber, cancellationToken);
            if (order is null)
            {
                return;
            }

            var dto = order.ToDetailsDto(clock);
            var model = OrderEmailModel.Build(dto, urls, business.Value, ordering.Value);

            var admins = emailOptions.Value.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (admins.Count > 0)
            {
                var adminEmail = renderer.Render(
                    EmailTemplates.AdminNewOrder,
                    $"🛎️ New {dto.FulfillmentName.ToLowerInvariant()} order {dto.OrderNumber} · {model["Total"]} · {model["ScheduledDate"]}",
                    model);
                foreach (var admin in admins)
                {
                    await email.QueueAsync(EmailMessage.Create(admin, adminEmail, replyTo: dto.Email), cancellationToken);
                }
            }
            else
            {
                logger.LogWarning("No admin recipients configured (Email:AdminRecipients); new order {OrderNumber} was not emailed to admins", dto.OrderNumber);
            }

            var customerEmail = renderer.Render(
                EmailTemplates.OrderConfirmation,
                $"ধন্যবাদ! Your order {dto.OrderNumber} has been received",
                model);
            await email.QueueAsync(EmailMessage.Create(dto.Email, customerEmail, emailOptions.Value.ReplyToAddress), cancellationToken);
        }
        catch (Exception ex)
        {
            // Never fail the checkout because a notification could not be prepared.
            logger.LogError(ex, "Failed to queue emails for order {OrderNumber}", notification.OrderNumber);
        }
    }
}

/// <summary>Keeps customers updated as their order progresses and alerts admins about customer cancellations.</summary>
internal sealed class OrderStatusChangedEmailHandler(
    IOrderRepository orders,
    IEmailTemplateRenderer renderer,
    IEmailService email,
    IAppUrls urls,
    IDateTimeProvider clock,
    IOptions<EmailOptions> emailOptions,
    IOptions<BusinessOptions> business,
    IOptions<OrderingOptions> ordering,
    ILogger<OrderStatusChangedEmailHandler> logger) : INotificationHandler<OrderStatusChangedNotification>
{
    public async Task Handle(OrderStatusChangedNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            var order = await orders.GetByNumberAsync(notification.OrderNumber, cancellationToken);
            if (order is null)
            {
                return;
            }

            var dto = order.ToDetailsDto(clock);
            var model = OrderEmailModel.Build(dto, urls, business.Value, ordering.Value);
            model["StatusNote"] = notification.Note;
            model["IsCancelled"] = notification.NewStatus == OrderStatus.Cancelled;
            model["IsReadyForPickup"] = notification.NewStatus == OrderStatus.ReadyForPickup;

            if (notification.NotifyCustomer && emailOptions.Value.SendCustomerStatusUpdates)
            {
                var subject = notification.NewStatus switch
                {
                    OrderStatus.Confirmed => $"Your order {dto.OrderNumber} is confirmed",
                    OrderStatus.ReadyForPickup => $"Your order {dto.OrderNumber} is ready for pickup",
                    OrderStatus.OutForDelivery => $"Your order {dto.OrderNumber} is on its way",
                    OrderStatus.Completed => $"Thank you! Order {dto.OrderNumber} is complete",
                    OrderStatus.Cancelled => $"Your order {dto.OrderNumber} has been cancelled",
                    _ => $"Update on your order {dto.OrderNumber}: {dto.StatusName}",
                };

                var rendered = renderer.Render(EmailTemplates.OrderStatusUpdate, subject, model);
                await email.QueueAsync(EmailMessage.Create(dto.Email, rendered, emailOptions.Value.ReplyToAddress), cancellationToken);
            }

            var admins = emailOptions.Value.AdminRecipients.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            if (notification.ChangedByCustomer && notification.NewStatus == OrderStatus.Cancelled && admins.Count > 0)
            {
                var rendered = renderer.Render(EmailTemplates.AdminOrderCancelled, $"❌ Order {dto.OrderNumber} was cancelled by the customer", model);
                foreach (var admin in admins)
                {
                    await email.QueueAsync(EmailMessage.Create(admin, rendered, replyTo: dto.Email), cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to queue status emails for order {OrderNumber}", notification.OrderNumber);
        }
    }
}
