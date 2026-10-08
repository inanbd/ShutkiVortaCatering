using System.Net;
using System.Text;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Application.Features.Orders;

/// <summary>Builds the template values shared by every order-related email.</summary>
internal static class OrderEmailModel
{
    public static Dictionary<string, object?> Build(
        OrderDetailsDto order,
        IAppUrls urls,
        BusinessOptions business,
        OrderingOptions ordering)
    {
        var firstName = order.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? order.CustomerName;

        return new Dictionary<string, object?>
        {
            ["OrderNumber"] = order.OrderNumber,
            ["CustomerName"] = order.CustomerName,
            ["CustomerFirstName"] = firstName,
            ["CustomerEmail"] = order.Email,
            ["CustomerPhone"] = order.Phone,
            ["FulfillmentName"] = order.FulfillmentName,
            ["IsDelivery"] = order.IsDelivery,
            ["IsPickup"] = !order.IsDelivery,
            ["DeliveryAddress"] = order.DeliveryAddress,
            ["ScheduledDate"] = Format.Date(order.ScheduledFor),
            ["ScheduledTime"] = Format.Time(order.ScheduledFor),
            ["PlacedAt"] = Format.DateTime(order.CreatedAtLocal),
            ["ItemsTable"] = new RawHtml(BuildItemsTable(order)),
            ["Subtotal"] = Format.Currency(order.Subtotal),
            ["DeliveryFee"] = Format.Currency(order.DeliveryFee),
            ["HasDeliveryFee"] = order.IsDelivery,
            ["Tax"] = Format.Currency(order.Tax),
            ["Total"] = Format.Currency(order.Total),
            ["CustomerNotes"] = order.CustomerNotes,
            ["StatusName"] = order.StatusName,
            ["StatusMessage"] = order.StatusMessage,
            ["OrderUrl"] = urls.OrderStatus(order.OrderNumber, order.TrackingToken),
            ["AdminOrderUrl"] = urls.AdminOrder(order.OrderNumber),
            ["PaymentInstructions"] = ordering.PaymentInstructions,
            ["PickupInstructions"] = business.PickupInstructions,
            ["PickupLocation"] = business.FullAddress,
        };
    }

    /// <summary>Email-safe (table based, inline styled) list of ordered items.</summary>
    private static string BuildItemsTable(OrderDetailsDto order)
    {
        var sb = new StringBuilder();
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#3b2416;\">");
        sb.Append("<tr>")
          .Append("<th align=\"left\" style=\"padding:10px 8px;border-bottom:2px solid #c8932b;font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#7a1f2b;\">Item</th>")
          .Append("<th align=\"center\" style=\"padding:10px 8px;border-bottom:2px solid #c8932b;font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#7a1f2b;\">Qty</th>")
          .Append("<th align=\"right\" style=\"padding:10px 8px;border-bottom:2px solid #c8932b;font-size:12px;letter-spacing:1px;text-transform:uppercase;color:#7a1f2b;\">Amount</th>")
          .Append("</tr>");

        foreach (var line in order.Lines)
        {
            sb.Append("<tr>")
              .Append("<td style=\"padding:12px 8px;border-bottom:1px dashed #e2cfa6;\">")
              .Append("<strong>").Append(WebUtility.HtmlEncode(line.ItemName)).Append("</strong>");
            if (!string.IsNullOrWhiteSpace(line.ItemBengaliName))
            {
                sb.Append("<br><span style=\"color:#8a6d4b;font-size:13px;\">").Append(WebUtility.HtmlEncode(line.ItemBengaliName)).Append("</span>");
            }

            sb.Append("<br><span style=\"color:#8a6d4b;font-size:12px;\">")
              .Append(WebUtility.HtmlEncode(Format.Currency(line.UnitPrice))).Append(" / ").Append(WebUtility.HtmlEncode(line.Unit))
              .Append("</span></td>")
              .Append("<td align=\"center\" style=\"padding:12px 8px;border-bottom:1px dashed #e2cfa6;white-space:nowrap;\">")
              .Append(WebUtility.HtmlEncode(line.QuantityText)).Append("</td>")
              .Append("<td align=\"right\" style=\"padding:12px 8px;border-bottom:1px dashed #e2cfa6;white-space:nowrap;\">")
              .Append(WebUtility.HtmlEncode(Format.Currency(line.LineTotal))).Append("</td>")
              .Append("</tr>");
        }

        sb.Append("</table>");
        return sb.ToString();
    }
}
