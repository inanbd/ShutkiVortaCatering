namespace ShutkiVorta.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;

    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(1, page), pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));

    /// <summary>
    /// The page to show of <paramref name="totalCount"/> rows: a page past the end (an old link, a made-up number) is the
    /// last page, and with no rows at all the first.
    /// </summary>
    public static int Within(int page, int totalCount, int pageSize) =>
        Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize))));

    /// <summary>The rows before <paramref name="page"/>: a long, so no page number can overflow into a negative OFFSET.</summary>
    public static long Skip(int page, int pageSize) => (Math.Max(1, page) - 1L) * pageSize;
}
