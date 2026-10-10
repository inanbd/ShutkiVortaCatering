namespace ShutkiVorta.Domain.Inventory;

/// <summary>One row of a purchase as entered: the item's name, how much was bought and what it cost in total.</summary>
public sealed record InventoryLineRequest(string ItemName, decimal Quantity, string Unit, decimal Price);
