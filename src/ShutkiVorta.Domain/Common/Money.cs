namespace ShutkiVorta.Domain.Common;

public static class Money
{
    /// <summary>Rounds a currency amount to cents using commercial (away-from-zero) rounding.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
