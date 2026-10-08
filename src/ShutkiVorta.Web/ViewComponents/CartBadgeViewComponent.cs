using Microsoft.AspNetCore.Mvc;
using ShutkiVorta.Web.Services;

namespace ShutkiVorta.Web.ViewComponents;

public sealed class CartBadgeViewComponent(CartService cart) : ViewComponent
{
    public IViewComponentResult Invoke() => View(cart.ItemCount);
}
