namespace ShutkiVorta.Application.Common.Email;

/// <summary>What happened to an email: which method delivered it and a human-readable detail (server reply or file path).</summary>
public sealed record EmailDeliveryResult(string Method, bool ActuallySent, string Detail);

public sealed record SmtpConnectionTestStep(string Name, bool Succeeded, string Detail);

public sealed record SmtpConnectionTestResult(bool Succeeded, string Summary, IReadOnlyList<SmtpConnectionTestStep> Steps, string? Hint);

/// <summary>A delivery failure with a plain-English hint about the most likely cause (wrong port, password, certificate...).</summary>
public sealed class EmailDeliveryException(string message, string? hint, Exception? inner = null) : Exception(message, inner)
{
    public string? Hint { get; } = hint;
}
