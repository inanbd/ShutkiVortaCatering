namespace ShutkiVorta.Domain.Wholesale;

public enum StandingOrderStatus
{
    PendingApproval = 0,
    Active = 1,
    Paused = 2,
    Cancelled = 3,
    Declined = 4,
}

public static class StandingOrderStatusExtensions
{
    public static string DisplayName(this StandingOrderStatus status) => status switch
    {
        StandingOrderStatus.PendingApproval => "Awaiting approval",
        StandingOrderStatus.Active => "Active",
        StandingOrderStatus.Paused => "Paused",
        StandingOrderStatus.Cancelled => "Cancelled",
        StandingOrderStatus.Declined => "Declined",
        _ => status.ToString(),
    };

    public static bool IsFinal(this StandingOrderStatus status) =>
        status is StandingOrderStatus.Cancelled or StandingOrderStatus.Declined;
}

/// <summary>What happened to one scheduled delivery date of a standing order (the idempotency ledger for generation).</summary>
public enum OccurrenceStatus
{
    Generated = 0,
    Skipped = 1,
    Cancelled = 2,
}
