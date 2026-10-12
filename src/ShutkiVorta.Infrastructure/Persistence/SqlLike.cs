namespace ShutkiVorta.Infrastructure.Persistence;

/// <summary>
/// Search terms for <c>LIKE … ESCAPE '\'</c>, the same on both providers. Wildcards a person types are escaped, not dropped:
/// people search by email, and emails often have an underscore.
/// </summary>
internal static class SqlLike
{
    /// <summary>A pattern matching text that contains <paramref name="term"/> (trimmed), its wildcards taken literally.</summary>
    public static string Contains(string term) => $"%{Escape(term.Trim())}%";

    /// <summary>
    /// The text with LIKE's wildcards made literal for <c>ESCAPE '\'</c>: % and _ on both providers, and SQL Server's [.
    /// </summary>
    public static string Escape(string text) =>
        text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");
}
