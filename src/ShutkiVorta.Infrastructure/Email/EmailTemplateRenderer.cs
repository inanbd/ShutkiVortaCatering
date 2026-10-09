using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ShutkiVorta.Application.Common.Email;
using ShutkiVorta.Application.Common.Interfaces;
using ShutkiVorta.Application.Common.Options;

namespace ShutkiVorta.Infrastructure.Email;

/// <summary>
/// Tiny, dependency-free template engine for the embedded HTML email templates.
/// Syntax: {{Key}} (HTML-encoded), {{{Key}}} (raw), {{#if Key}}...{{else}}...{{/if}} (truthy: true, non-empty text, non-null).
/// Every template is wrapped in _Layout.html, which provides the branded Bangladeshi header and footer.
/// </summary>
internal sealed partial class EmailTemplateRenderer(
    IOptionsMonitor<BusinessOptions> business,
    IAppUrls urls) : IEmailTemplateRenderer
{
    private const string LayoutName = "_Layout";
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public RenderedEmail Render(string templateName, string subject, IReadOnlyDictionary<string, object?> model)
    {
        var values = BuildValues(subject, model);
        var body = Merge(LoadTemplate(templateName), values);

        values["Body"] = new RawHtml(body);
        var html = Merge(LoadTemplate(LayoutName), values);

        return new RenderedEmail(subject, html, HtmlToText(body, values));
    }

    private Dictionary<string, object?> BuildValues(string subject, IReadOnlyDictionary<string, object?> model)
    {
        var b = business.CurrentValue;
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Subject"] = subject,
            ["Preheader"] = subject,
            ["BusinessName"] = b.Name,
            ["BusinessBengaliName"] = b.BengaliName,
            ["BusinessTagline"] = b.Tagline,
            ["BusinessPhone"] = b.Phone,
            ["BusinessPhoneHref"] = "tel:" + new string(b.Phone.Where(c => char.IsDigit(c) || c == '+').ToArray()),
            ["BusinessEmail"] = b.Email,
            ["BusinessAddress"] = b.FullAddress,
            ["BusinessHours"] = b.OpeningHoursText,
            ["SiteUrl"] = urls.Home(),
            ["MenuUrl"] = urls.Menu(),
            ["LogoUrl"] = urls.Absolute("/images/email-logo.png"),
            ["Year"] = DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture),
        };

        foreach (var (key, value) in model)
        {
            values[key] = value;
        }

        return values;
    }

    private static string Merge(string template, IReadOnlyDictionary<string, object?> values)
    {
        // Conditionals first (non-nested), so their contents can still contain placeholders.
        var result = IfBlock().Replace(template, m =>
        {
            var truthy = IsTruthy(values.TryGetValue(m.Groups["key"].Value, out var v) ? v : null);
            return truthy ? m.Groups["then"].Value : m.Groups["else"].Success ? m.Groups["else"].Value : string.Empty;
        });

        result = RawPlaceholder().Replace(result, m =>
            values.TryGetValue(m.Groups["key"].Value, out var v) ? Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty);

        return Placeholder().Replace(result, m =>
        {
            if (!values.TryGetValue(m.Groups["key"].Value, out var v) || v is null)
            {
                return string.Empty;
            }

            return v is RawHtml raw ? raw.Value : WebUtility.HtmlEncode(Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty);
        });
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s),
        RawHtml r => !string.IsNullOrWhiteSpace(r.Value),
        _ => true,
    };

    private static string LoadTemplate(string name) => Cache.GetOrAdd(name, static n =>
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = $"{typeof(EmailTemplateRenderer).Namespace}.Templates.{n}.html";
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Email template '{n}' was not found (resource '{resource}').");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>Plain-text alternative for clients that do not render HTML (and for spam-filter friendliness).</summary>
    private static string HtmlToText(string bodyHtml, IReadOnlyDictionary<string, object?> values)
    {
        var text = StyleOrHead().Replace(bodyHtml, string.Empty);
        text = Anchor().Replace(text, m =>
        {
            var label = Tags().Replace(m.Groups["text"].Value, string.Empty).Trim();
            var href = m.Groups["href"].Value;
            return string.IsNullOrEmpty(label) || label == href ? href : $"{label} ({href})";
        });
        text = LineBreaks().Replace(text, "\n");
        text = Tags().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = HorizontalSpace().Replace(text, " ");
        text = string.Join("\n", text.Split('\n').Select(l => l.Trim()));
        text = BlankLines().Replace(text, "\n\n").Trim();

        var footer = $"\n\n—\n{values["BusinessName"]} · {values["BusinessPhone"]} · {values["SiteUrl"]}";
        return text + footer;
    }

    [GeneratedRegex(@"\{\{#if (?<key>\w+)\}\}(?<then>.*?)(?:\{\{else\}\}(?<else>.*?))?\{\{/if\}\}", RegexOptions.Singleline)]
    private static partial Regex IfBlock();

    [GeneratedRegex(@"\{\{\{(?<key>\w+)\}\}\}")]
    private static partial Regex RawPlaceholder();

    [GeneratedRegex(@"\{\{(?<key>\w+)\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"<(style|head)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex StyleOrHead();

    [GeneratedRegex("<a [^>]*href=\"(?<href>[^\"]+)\"[^>]*>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<br\s*/?>|</p>|</tr>|</h[1-6]>|</div>|</li>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreaks();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex HorizontalSpace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
