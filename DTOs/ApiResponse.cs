using System;
using System.Collections.Generic;

namespace Ideax.DTOs;

/// <summary>
/// Standard API response wrapper. Endpoints should return ApiResponse or ApiResponse&lt;T&gt;.
/// </summary>
public class ApiResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public IEnumerable<string>? Errors { get; set; }
    public object? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse Ok(object? data = null, string? message = null)
        => new ApiResponse { Success = true, Data = data, Message = message };

    public static ApiResponse Fail(string? message = null, IEnumerable<string>? errors = null)
        => new ApiResponse { Success = false, Message = message, Errors = errors };
}

/// <summary>
/// Generic standard API response wrapper with typed Data.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public IEnumerable<string>? Errors { get; set; }
    public T? Data { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> Ok(T? data = default, string? message = null)
        => new ApiResponse<T> { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string? message = null, IEnumerable<string>? errors = null)
        => new ApiResponse<T> { Success = false, Message = message, Errors = errors };
}
