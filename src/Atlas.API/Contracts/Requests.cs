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

    /// <summary>Only members of this department.</summary>
    [FromQuery(Name = "departmentId")] public Guid? DepartmentId { get; init; }

    /// <summary>Only users holding this profile.</summary>
    [FromQuery(Name = "profileId")] public Guid? ProfileId { get; init; }

    /// <summary>true: only orphans (ordinary accounts with no department and no profile).</summary>
    [FromQuery(Name = "orphan")] public bool? Orphan { get; init; }
}

public sealed record CreateProfileRequest
{
    /// <example>Analyst</example>
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed record UpdateProfileRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class ListProfilesRequest
{
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")] public int PageSize { get; init; } = 20;

    /// <summary>Case-insensitive substring of the profile name.</summary>
    [FromQuery(Name = "search")] public string? Search { get; init; }

    /// <summary>true: held by at least one user; false: unassigned (retained) profiles only.</summary>
    [FromQuery(Name = "assigned")] public bool? Assigned { get; init; }
}

public sealed record AssignProfileRequest
{
    public Guid ProfileId { get; init; }
}

public sealed record CreateDepartmentRequest
{
    /// <example>Operations</example>
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed record UpdateDepartmentRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public sealed class ListDepartmentsRequest
{
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")] public int PageSize { get; init; } = 20;

    /// <summary>Case-insensitive substring of the department name.</summary>
    [FromQuery(Name = "search")] public string? Search { get; init; }
}

public sealed class ListDepartmentMembersRequest
{
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")] public int PageSize { get; init; } = 20;
}

public sealed record SetDepartmentHeadRequest
{
    /// <summary>The new head. Must already be a member of the department.</summary>
    public Guid UserId { get; init; }
}

public sealed record AssignDepartmentRequest
{
    /// <summary>The department the user should belong to (replacing any current membership).</summary>
    public Guid DepartmentId { get; init; }

    /// <summary>Optional: give the user this profile in the same operation (see the endpoint remarks for the profile rules).</summary>
    public Guid? ProfileId { get; init; }
}
