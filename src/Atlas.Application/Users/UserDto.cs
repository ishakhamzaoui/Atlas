using Atlas.Domain.Users;

namespace Atlas.Application.Users;

/// <summary>Safe projection of a user. Never contains the password hash or security stamp.</summary>
public sealed record UserDto(
    Guid Id,
    string Username,
    string Role,
    bool IsActive,
    bool MustChangePassword,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public static class UserMappings
{
    public static UserDto ToDto(this User user) =>
        new(user.Id, user.Username, user.Role.ToString(), user.IsActive, user.MustChangePassword, user.CreatedAt, user.UpdatedAt);
}
