using Microsoft.AspNetCore.Html;
using ShutkiVorta.Application.Features.Customers;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

/// <summary>Paths and labels for the customer pages. Links carry account ids or order numbers, never an email.</summary>
public static class CustomersUi
{
    public const string CustomersPath = "/admin/customers";

    /// <summary>The order value is what was ordered, not what was paid.</summary>
    public const string OrderValueHint = "The total of their orders that were not cancelled, paid or not: payments are not tracked.";

    public static string AccountPath(string userId) => $"{CustomersPath}/{Uri.EscapeDataString(userId)}";

    /// <summary>A guest's page, found by one of their order numbers.</summary>
    public static string GuestPath(string orderNumber) => $"{CustomersPath}/guest/{Uri.EscapeDataString(orderNumber)}";

    public static string DetailsPath(CustomerListItemDto customer) =>
        customer.AccountId is { } id ? AccountPath(id) : GuestPath(customer.GuestOrderNumber ?? string.Empty);

    public static string OrderPath(string orderNumber) => $"/admin/orders/{Uri.EscapeDataString(orderNumber)}";

    public static string StandingOrderPath(int id) => $"/admin/recurring/{id}";

    public static string StaffPath(string userId) => $"/admin/users/{Uri.EscapeDataString(userId)}";

    public const string InquiriesPath = "/admin/inquiries";

    /// <summary>"Account", "Guest" or "Restaurant" as a coloured pill.</summary>
    public static IHtmlContent TypePill(CustomerType type) => new HtmlString(type switch
    {
        CustomerType.Guest => "<span class=\"pill pill-guest\">Guest</span>",
        CustomerType.Restaurant => "<span class=\"pill pill-restaurant\">Restaurant</span>",
        _ => "<span class=\"pill pill-account\">Account</span>",
    });

    /// <summary>The list's tabs: everyone, then each type.</summary>
    public static IReadOnlyList<(CustomerType? Type, string Label)> Tabs { get; } =
    [
        (null, "All"),
        (CustomerType.Account, "Accounts"),
        (CustomerType.Guest, "Guests"),
        (CustomerType.Restaurant, "Restaurants"),
    ];

    public static int TabCount(CustomerListDto list, CustomerType? type) => type switch
    {
        CustomerType.Account => list.AccountCount,
        CustomerType.Guest => list.GuestCount,
        CustomerType.Restaurant => list.RestaurantCount,
        _ => list.TotalCount,
    };

    /// <summary>A message shortened to about <paramref name="length"/> characters at a word break.</summary>
    public static string Shorten(string text, int length = 240)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length <= length)
        {
            return flat;
        }

        var cut = flat.LastIndexOf(' ', length);
        return flat[..(cut > length / 2 ? cut : length)] + "…";
    }
}
