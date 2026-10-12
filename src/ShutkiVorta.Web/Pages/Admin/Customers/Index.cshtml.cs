using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using ShutkiVorta.Application.Common.Formatting;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Security;
using ShutkiVorta.Application.Features.Customers;
using ShutkiVorta.Web.Infrastructure;

namespace ShutkiVorta.Web.Pages.Admin.Customers;

/// <summary>
/// Who orders from the kitchen: accounts that are customers, restaurants and guests (grouped by email), with their orders,
/// order value and last order. Read-only.
/// </summary>
[RequirePermission(Permissions.CustomersView)]
public sealed class IndexModel(ISender sender, IDateTimeProvider clock) : AppPageModel(sender)
{
    public const int PageSize = 25;

    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }

    /// <summary>account, guest or restaurant (empty: everyone).</summary>
    [BindProperty(SupportsGet = true)] public CustomerType? Type { get; set; }

    [BindProperty(SupportsGet = true)] public CustomerSort Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")] public int PageNumber { get; set; } = 1;

    public CustomerListDto List { get; private set; } = new();

    public bool IsSearching => !string.IsNullOrWhiteSpace(Search);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Customers";
        if (Type is { } type && !Enum.IsDefined(type))
        {
            Type = null;
        }

        if (!Enum.IsDefined(Sort))
        {
            Sort = CustomerSort.LastOrder;
        }

        List = await Sender.Send(new GetCustomersQuery
        {
            Search = Search,
            Type = Type,
            Sort = Sort,
            Page = PageNumber,
            PageSize = PageSize,
        }, cancellationToken);
    }

    public DateTime? Local(DateTime? utc) => utc is { } value ? clock.ToBusinessTime(value) : null;

    /// <summary>This list with the current search and sort, on another tab (first page).</summary>
    public string TabUrl(CustomerType? type) => ListUrl(type, Sort, page: 1);

    public string PageUrl(int page) => ListUrl(Type, Sort, page);

    /// <summary>This tab without the search and with the default sort.</summary>
    public string ResetUrl => ListUrl(Type, CustomerSort.LastOrder, page: 1, keepSearch: false);

    private string ListUrl(CustomerType? type, CustomerSort sort, int page, bool keepSearch = true) => QueryHelpers.AddQueryString(CustomersUi.CustomersPath, new Dictionary<string, string?>
    {
        ["type"] = type?.ToString().ToLowerInvariant(),
        ["q"] = keepSearch && IsSearching ? Search!.Trim() : null,
        ["sort"] = sort == CustomerSort.LastOrder ? null : sort.ToString().ToLowerInvariant(),
        ["p"] = page > 1 ? page.ToString(Format.Culture) : null,
    }.Where(kv => kv.Value is not null));
}
