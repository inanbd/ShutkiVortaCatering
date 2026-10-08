namespace ShutkiVorta.Application.Common.Interfaces;

public interface IOrderNumberGenerator
{
    /// <summary>Creates a short, human-friendly order number such as "SV-261008-7KQ4".</summary>
    string NewOrderNumber(DateTime businessNow);

    /// <summary>Creates a reference for a restaurant standing order, e.g. "RO-2610-7KQ4".</summary>
    string NewStandingOrderReference(DateTime businessNow);

    /// <summary>Creates an unguessable token for guest order-status links.</summary>
    string NewTrackingToken();
}
