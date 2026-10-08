namespace ShutkiVorta.Domain.Common;

/// <summary>Raised when a business rule is violated inside the domain model.</summary>
public sealed class DomainException(string message) : Exception(message);
