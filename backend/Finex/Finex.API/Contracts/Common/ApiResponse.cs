using System.Diagnostics;

namespace Finex.API.Contracts.Common;

public class ApiResponse
{
    public bool Success { get; init; } = true;
    public string Message { get; init; } = "Operation completed successfully.";
    public string? TraceId { get; init; } = Activity.Current?.TraceId.ToString();
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponse Ok(string message = "Operation completed successfully.")
        => new() { Success = true, Message = message };
}

public class ApiResponse<T> : ApiResponse
{
    public T? Data { get; init; }

    public static ApiResponse<T> Ok(T data, string message = "Operation completed successfully.")
        => new() { Success = true, Data = data, Message = message };
}

public sealed class PagedResponse<T> : ApiResponse<IReadOnlyList<T>>
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    public static PagedResponse<T> Ok(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount,
        string message = "Data retrieved successfully.")
        => new()
        {
            Success = true,
            Data = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Message = message
        };
}