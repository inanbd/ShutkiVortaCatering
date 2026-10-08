using ShutkiVorta.Application.Common.Email;

namespace ShutkiVorta.Application.Common.Interfaces;

public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renders an HTML template (wrapped in the branded layout) and a plain-text alternative.
    /// String values are HTML-encoded; <see cref="RawHtml"/> values are inserted as-is.
    /// </summary>
    RenderedEmail Render(string templateName, string subject, IReadOnlyDictionary<string, object?> model);
}
