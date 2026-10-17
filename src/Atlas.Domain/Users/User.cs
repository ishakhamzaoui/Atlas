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

    /// <summary>Optional. A user belongs to at most one department; null means "not in any department".</summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>Optional. The profile that defines this user's privileges; null means "no profile".</summary>
    public Guid? ProfileId { get; private set; }

    /// <summary>
    /// An ordinary account with no department and no profile: it may authenticate, but only reads the public space.
    /// (Administrator accounts are outside the organization and are never "orphans".)
    /// </summary>
    public bool IsOrphan => Role == UserRole.User && DepartmentId is null && ProfileId is null;

    /// <summary>An ordinary account that is in a department AND holds a profile.</summary>
    public bool HasOrganizationalPrivileges => Role == UserRole.User && DepartmentId is not null && ProfileId is not null;

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
        if (role == UserRole.Admin && (DepartmentId is not null || ProfileId is not null))
            throw new DomainException("remove_organization_assignments_first",
                "Remove the user's department and profile before making the account an administrator.");
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

    // ---- organization (assignments are independent of the account's identity and credentials; no stamp rotation, ----
    // ---- because effective access is evaluated from the current state on every request, not read from the token) ----

    public void AssignToDepartment(Guid departmentId, DateTimeOffset now)
    {
        RequireOrdinaryAccount();
        if (DepartmentId == departmentId) return;
        DepartmentId = departmentId;
        Touch(now, rotateStamp: false);
    }

    public void RemoveFromDepartment(DateTimeOffset now)
    {
        if (DepartmentId is null) return;
        DepartmentId = null;
        Touch(now, rotateStamp: false);
    }

    public void AssignProfile(Guid profileId, DateTimeOffset now)
    {
        RequireOrdinaryAccount();
        if (ProfileId == profileId) return;
        ProfileId = profileId;
        Touch(now, rotateStamp: false);
    }

    /// <summary>Detaches the profile from this user. The profile itself is retained.</summary>
    public void RemoveProfile(DateTimeOffset now)
    {
        if (ProfileId is null) return;
        ProfileId = null;
        Touch(now, rotateStamp: false);
    }

    private void RequireOrdinaryAccount()
    {
        if (Role == UserRole.Admin)
            throw new DomainException("admin_not_assignable",
                "Administrator accounts are outside the organization and cannot receive a department or profile.");
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
