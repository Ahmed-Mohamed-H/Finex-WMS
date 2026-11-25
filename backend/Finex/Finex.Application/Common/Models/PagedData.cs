namespace Finex.Application.Common.Models
{
    public sealed record PagedData<T>(
     IReadOnlyList<T> Items,
     int Page,
     int PageSize,
     int TotalCount);
}