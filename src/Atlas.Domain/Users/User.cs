using Atlas.Domain.Common;

namespace Atlas.Domain.Users;

public sealed class User
{
    private User() { } // EF Core

    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string NormalizedUsername { get; private set; } = default!;
    public string PasswordHash { get; private set; } = default!;
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public bool MustChangePassword { get; private set; }

    /// <summary>Rotated on every security-relevant change; embedded in JWTs so old tokens stop working.</summary>
    public Guid SecurityStamp { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? PasswordChangedAt { get; private set; }

    public static User Create(string username, string passwordHash, UserRole role, bool mustChangePassword, DateTimeOffset now)
    {
        var name = RequireValidUsername(username);
        RequireHash(passwordHash);
        RequireRole(role);

        return new User
        {
            Id = Guid.CreateVersion7(),
            Username = name,
            NormalizedUsername = UsernamePolicy.Normalize(name),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            MustChangePassword = mustChangePassword,
            SecurityStamp = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Rename(string username, DateTimeOffset now)
    {
        var name = RequireValidUsername(username);
        Username = name;
        NormalizedUsername = UsernamePolicy.Normalize(name);
        Touch(now, rotateStamp: true);
    }

    public void ChangeRole(UserRole role, DateTimeOffset now)
    {
        RequireRole(role);
        if (Role == role) return;
        Role = role;
        Touch(now, rotateStamp: true);
    }

    public void Activate(DateTimeOffset now)
    {
        if (IsActive) return;
        IsActive = true;
        Touch(now, rotateStamp: false);
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive) return;
        IsActive = false;
        Touch(now, rotateStamp: true);
    }

    /// <summary>User-chosen password. Clears the forced-change flag.</summary>
    public void ChangePassword(string newPasswordHash, DateTimeOffset now)
    {
        RequireHash(newPasswordHash);
        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        PasswordChangedAt = now;
        Touch(now, rotateStamp: true);
    }

    /// <summary>Administrator-issued temporary password. Forces a change at next login.</summary>
    public void SetTemporaryPassword(string temporaryPasswordHash, DateTimeOffset now)
    {
        RequireHash(temporaryPasswordHash);
        PasswordHash = temporaryPasswordHash;
        MustChangePassword = true;
        PasswordChangedAt = now;
        Touch(now, rotateStamp: true);
    }

    private void Touch(DateTimeOffset now, bool rotateStamp)
    {
        UpdatedAt = now;
        if (rotateStamp) SecurityStamp = Guid.NewGuid();
    }

    private static string RequireValidUsername(string? username)
    {
        var errors = UsernamePolicy.Validate(username);
        if (errors.Count > 0)
            throw new DomainException("invalid_username", string.Join(" ", errors));
        return username!.Trim();
    }

    private static void RequireHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            throw new DomainException("invalid_password_hash", "A password hash is required.");
    }

    private static void RequireRole(UserRole role)
    {
        if (!Enum.IsDefined(role))
            throw new DomainException("invalid_role", "Unknown role.");
    }
}
