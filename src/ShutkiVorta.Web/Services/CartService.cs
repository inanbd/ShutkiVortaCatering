using System.Globalization;
using System.Text.Json;
using ShutkiVorta.Application.Features.Orders;

namespace ShutkiVorta.Web.Services;

/// <summary>
/// Keeps the visitor's cart (menu item ids + pounds) in a cookie. Prices are never stored client-side;
/// they are always recalculated from the live menu.
/// </summary>
public sealed class CartService(IHttpContextAccessor httpContextAccessor)
{
    public const string CookieName = "sv_cart";
    private const int MaxLines = 30;
    private const decimal MaxQuantity = 200m;

    private List<CartLineInput>? _lines;

    private HttpContext Context => httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");

    public IReadOnlyList<CartLineInput> Lines => _lines ??= Read();

    public int ItemCount => Lines.Count;

    public decimal QuantityOf(int menuItemId) => Lines.FirstOrDefault(l => l.MenuItemId == menuItemId)?.Quantity ?? 0;

    public void Add(int menuItemId, decimal quantity)
    {
        if (quantity <= 0)
        {
            return;
        }

        var lines = Lines.ToList();
        var index = lines.FindIndex(l => l.MenuItemId == menuItemId);
        if (index >= 0)
        {
            lines[index] = lines[index] with { Quantity = Math.Min(MaxQuantity, lines[index].Quantity + quantity) };
        }
        else if (lines.Count < MaxLines)
        {
            lines.Add(new CartLineInput(menuItemId, Math.Min(MaxQuantity, quantity)));
        }

        Save(lines);
    }

    public void SetQuantity(int menuItemId, decimal quantity)
    {
        var lines = Lines.ToList();
        var index = lines.FindIndex(l => l.MenuItemId == menuItemId);
        if (index < 0)
        {
            Add(menuItemId, quantity);
            return;
        }

        if (quantity <= 0)
        {
            lines.RemoveAt(index);
        }
        else
        {
            lines[index] = lines[index] with { Quantity = Math.Min(MaxQuantity, quantity) };
        }

        Save(lines);
    }

    public void Remove(int menuItemId) => Save(Lines.Where(l => l.MenuItemId != menuItemId).ToList());

    public void RemoveMany(IEnumerable<int> menuItemIds)
    {
        var ids = menuItemIds.ToHashSet();
        if (ids.Count > 0)
        {
            Save(Lines.Where(l => !ids.Contains(l.MenuItemId)).ToList());
        }
    }

    public void Clear()
    {
        _lines = [];
        Context.Response.Cookies.Delete(CookieName);
    }

    private List<CartLineInput> Read()
    {
        if (!Context.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            var stored = JsonSerializer.Deserialize<List<StoredLine>>(raw) ?? [];
            return stored
                .Where(s => s.I > 0 && decimal.TryParse(s.Q, NumberStyles.Number, CultureInfo.InvariantCulture, out var q) && q > 0)
                .Select(s => new CartLineInput(s.I, Math.Min(MaxQuantity, decimal.Parse(s.Q!, CultureInfo.InvariantCulture))))
                .GroupBy(l => l.MenuItemId)
                .Select(g => new CartLineInput(g.Key, g.Sum(l => l.Quantity)))
                .Take(MaxLines)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Save(List<CartLineInput> lines)
    {
        _lines = lines;
        if (lines.Count == 0)
        {
            Context.Response.Cookies.Delete(CookieName);
            return;
        }

        var json = JsonSerializer.Serialize(lines.Select(l => new StoredLine(l.MenuItemId, l.Quantity.ToString(CultureInfo.InvariantCulture))));
        Context.Response.Cookies.Append(CookieName, json, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
        });
    }

    private sealed record StoredLine(int I, string? Q);
}
