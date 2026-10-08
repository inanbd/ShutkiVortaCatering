namespace ShutkiVorta.Domain.Orders;

public sealed record DeliveryAddress(string Line1, string? Line2, string City, string State, string PostalCode)
{
    public override string ToString() =>
        string.Join(", ", new[] { Line1, Line2, City, $"{State} {PostalCode}" }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
