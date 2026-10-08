using ShutkiVorta.Domain.Menu;

namespace ShutkiVorta.Domain.Orders;

public sealed record OrderLineRequest(MenuItem Item, decimal Quantity);
