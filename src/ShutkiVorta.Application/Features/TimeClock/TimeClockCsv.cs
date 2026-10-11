using System.Text;

namespace ShutkiVorta.Application.Features.TimeClock;

/// <summary>
/// CSV files that spreadsheets open safely: every cell that could start a formula (= + - @, tab or carriage return) gets a
/// leading apostrophe, and cells are quoted as RFC 4180 requires. UTF-8 with a byte-order mark, so Excel shows Bengali names.
/// </summary>
internal static class TimeClockCsv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];
    private static readonly char[] NeedsQuotes = ['"', ',', '\r', '\n'];

    public static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && FormulaStarts.Contains(text[0]))
        {
            text = "'" + text;
        }

        return text.IndexOfAny(NeedsQuotes) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var csv = new StringBuilder();
        AppendRow(csv, header);
        foreach (var row in rows)
        {
            AppendRow(csv, row);
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string?> cells) =>
        csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
}
