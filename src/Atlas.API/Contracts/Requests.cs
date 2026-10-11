using System.ComponentModel.DataAnnotations;
using Atlas.Domain.Users;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Contracts;

/// <summary>Credentials. Username and password are the only login factors.</summary>
public sealed record LoginRequest
{
    /// <example>jdoe</example>
    public string Username { get; init; } = string.Empty;

    [DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;
}

public sealed record ChangePasswordRequest
{
    [DataType(DataType.Password)]
    public string CurrentPassword { get; init; } = string.Empty;

    [DataType(DataType.Password)]
    public string NewPassword { get; init; } = string.Empty;

    [DataType(DataType.Password)]
    public string ConfirmNewPassword { get; init; } = string.Empty;
}

public sealed record RefreshRequest
{
    /// <summary>The refresh token from the last login/refresh response. It is single-use.</summary>
    [DataType(DataType.Password)]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record LogoutRequest
{
    /// <summary>The refresh token whose session should be ended.</summary>
    [DataType(DataType.Password)]
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed record CreateUserRequest
{
    /// <example>jdoe</example>
    public string Username { get; init; } = string.Empty;

    /// <summary>Initial (temporary) password. The user must change it at first login.</summary>
    [DataType(DataType.Password)]
    public string Password { get; init; } = string.Empty;

    public UserRole Role { get; init; } = UserRole.User;
}

public sealed record UpdateUserRequest
{
    public string Username { get; init; } = string.Empty;
    public UserRole Role { get; init; } = UserRole.User;
}

public sealed record SetUserStatusRequest
{
    public bool IsActive { get; init; }
}

public sealed class ListUsersRequest
{
    /// <summary>1-based page number.</summary>
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;

    /// <summary>Page size, 1-100.</summary>
    [FromQuery(Name = "pageSize")] public int PageSize { get; init; } = 20;

    /// <summary>Case-insensitive substring of the username.</summary>
    [FromQuery(Name = "search")] public string? Search { get; init; }

    [FromQuery(Name = "role")] public UserRole? Role { get; init; }

    [FromQuery(Name = "isActive")] public bool? IsActive { get; init; }
}
