namespace ShutkiVorta.Application.Common.Email;

public sealed record EmailMessage(
    IReadOnlyList<string> To,
    string Subject,
    string HtmlBody,
    string TextBody,
    string? ReplyTo = null)
{
    public static EmailMessage Create(string to, RenderedEmail rendered, string? replyTo = null) =>
        new([to], rendered.Subject, rendered.Html, rendered.Text, replyTo);

    public static EmailMessage Create(IEnumerable<string> to, RenderedEmail rendered, string? replyTo = null) =>
        new(to.ToArray(), rendered.Subject, rendered.Html, rendered.Text, replyTo);
}

public sealed record RenderedEmail(string Subject, string Html, string Text);

/// <summary>Pre-built, trusted HTML that the template renderer must not encode.</summary>
public sealed record RawHtml(string Value)
{
    public override string ToString() => Value;
}
