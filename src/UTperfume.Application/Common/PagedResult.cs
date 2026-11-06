namespace UTperfume.Application.Common;

/// <summary>Pagination envelope (ADR-0008).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalItems, int TotalPages)
{
    public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalItems) =>
        new(items, page, pageSize, totalItems, totalItems == 0 ? 0 : (int)Math.Ceiling(totalItems / (double)pageSize));
}
