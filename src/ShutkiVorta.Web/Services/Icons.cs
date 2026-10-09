using Microsoft.AspNetCore.Html;

namespace ShutkiVorta.Web.Services;

/// <summary>Inline SVG icons (24×24, stroke based) so pages need no icon font or extra requests.</summary>
public static class Icons
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cart"] = "<path d=\"M3 4h2l2.4 11.2a2 2 0 0 0 2 1.6h7.7a2 2 0 0 0 2-1.5L21 8H6.2\"/><circle cx=\"10\" cy=\"20\" r=\"1.4\"/><circle cx=\"17\" cy=\"20\" r=\"1.4\"/>",
        ["user"] = "<circle cx=\"12\" cy=\"8\" r=\"4\"/><path d=\"M4 21c1.5-4 4.5-6 8-6s6.5 2 8 6\"/>",
        ["phone"] = "<path d=\"M5 4h3l2 5-2.5 1.5a11 11 0 0 0 6 6L15 14l5 2v3a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2z\"/>",
        ["mail"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"14\" rx=\"2\"/><path d=\"m3 7 9 6 9-6\"/>",
        ["pin"] = "<path d=\"M12 21s-7-6.3-7-12a7 7 0 0 1 14 0c0 5.7-7 12-7 12z\"/><circle cx=\"12\" cy=\"9\" r=\"2.5\"/>",
        ["clock"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
        ["truck"] = "<path d=\"M3 6h11v10H3zM14 9h4l3 3v4h-7\"/><circle cx=\"7\" cy=\"18\" r=\"1.8\"/><circle cx=\"17\" cy=\"18\" r=\"1.8\"/>",
        ["bag"] = "<path d=\"M5 8h14l-1 12H6L5 8z\"/><path d=\"M9 8V6a3 3 0 0 1 6 0v2\"/>",
        ["check"] = "<path d=\"m5 12.5 4.5 4.5L19 7.5\"/>",
        ["check-circle"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"m8 12.5 3 3 5.5-6\"/>",
        ["menu"] = "<path d=\"M4 7h16M4 12h16M4 17h16\"/>",
        ["calendar"] = "<rect x=\"3\" y=\"5\" width=\"18\" height=\"16\" rx=\"2\"/><path d=\"M3 10h18M8 3v4M16 3v4\"/>",
        ["leaf"] = "<path d=\"M5 19c0-8 5-14 15-14 0 10-6 15-14 15\"/><path d=\"M5 19c3-4 6-6 10-8\"/>",
        ["fire"] = "<path d=\"M12 21c4 0 7-2.7 7-6.5 0-4.5-4-6.5-4.5-10.5C11 6 9 9 9.5 12 8 11 7.5 9.5 7.5 8 5.8 9.8 5 12 5 14.5 5 18.3 8 21 12 21z\"/>",
        ["heart"] = "<path d=\"M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10z\"/>",
        ["arrow-right"] = "<path d=\"M5 12h14M13 6l6 6-6 6\"/>",
        ["trash"] = "<path d=\"M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3\"/>",
        ["logout"] = "<path d=\"M15 4h4v16h-4M10 8l-4 4 4 4M6 12h10\"/>",
        ["dashboard"] = "<rect x=\"3\" y=\"3\" width=\"7\" height=\"9\" rx=\"1\"/><rect x=\"14\" y=\"3\" width=\"7\" height=\"5\" rx=\"1\"/><rect x=\"14\" y=\"12\" width=\"7\" height=\"9\" rx=\"1\"/><rect x=\"3\" y=\"16\" width=\"7\" height=\"5\" rx=\"1\"/>",
        ["receipt"] = "<path d=\"M6 3h12v18l-3-2-3 2-3-2-3 2V3z\"/><path d=\"M9 8h6M9 12h6\"/>",
        ["bowl"] = "<path d=\"M3 11h18a9 9 0 0 1-18 0z\"/><path d=\"M8 7c0-1.5 1-2 1-3M12 7c0-1.5 1-2 1-3M16 7c0-1.5 1-2 1-3\"/>",
        ["repeat"] = "<path d=\"M17 2l3 3-3 3\"/><path d=\"M4 11V9a4 4 0 0 1 4-4h12\"/><path d=\"M7 22l-3-3 3-3\"/><path d=\"M20 13v2a4 4 0 0 1-4 4H4\"/>",
        ["pot"] = "<path d=\"M4 10h16v6a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4v-6z\"/><path d=\"M2 10h20M9 6.5c0-1 .8-1.5.8-2.5M14 6.5c0-1 .8-1.5.8-2.5\"/>",
        ["chat"] = "<path d=\"M4 5h16v11H9l-5 4V5z\"/>",
        ["users"] = "<circle cx=\"9\" cy=\"8\" r=\"3.5\"/><path d=\"M2.5 20c1-3.5 3.5-5 6.5-5s5.5 1.5 6.5 5\"/><circle cx=\"17\" cy=\"9\" r=\"2.5\"/><path d=\"M16 14c2.5 0 4.5 1.3 5.5 4\"/>",
        ["settings"] = "<circle cx=\"12\" cy=\"12\" r=\"3\"/><path d=\"M12 2v3M12 19v3M4.2 4.2l2.1 2.1M17.7 17.7l2.1 2.1M2 12h3M19 12h3M4.2 19.8l2.1-2.1M17.7 6.3l2.1-2.1\"/>",
        ["external"] = "<path d=\"M14 4h6v6M20 4l-9 9M18 14v6H4V6h6\"/>",
        ["search"] = "<circle cx=\"11\" cy=\"11\" r=\"7\"/><path d=\"m20 20-4-4\"/>",
        ["print"] = "<path d=\"M7 9V3h10v6M7 17H4v-7h16v7h-3M7 14h10v7H7z\"/>",
        ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",
        ["edit"] = "<path d=\"M4 20h4L19 9l-4-4L4 16v4z\"/>",
        ["eye"] = "<path d=\"M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7S2 12 2 12z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
    };

    public static IHtmlContent Get(string name, string? cssClass = null) =>
        new HtmlString(
            $"<svg class=\"icon {cssClass}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.8\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\" focusable=\"false\">{(Paths.TryGetValue(name, out var p) ? p : string.Empty)}</svg>");

    /// <summary>Filled red chili used for the spice meter.</summary>
    public static IHtmlContent Chili(bool on) =>
        new HtmlString(
            $"<svg class=\"{(on ? "on" : "off")}\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" focusable=\"false\"><path fill=\"currentColor\" d=\"M14.5 6.2c2.6.2 4.6 2.4 4.4 5.2-.4 5.2-6.6 9.4-13.4 9.1-1 0-1.1-1.2-.2-1.5 4.5-1.6 6.7-4.6 7-8.3.1-2.3.4-4.6 2.2-4.5z\"/><path fill=\"#0d4d3a\" d=\"M14.2 6.4c-.2-1.6.4-3 1.9-3.9.5-.3 1 .3.7.7-.7.9-1 1.9-.8 3.1-.6-.1-1.2-.1-1.8.1z\"/></svg>");
}
