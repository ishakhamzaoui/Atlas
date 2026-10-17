using Atlas.Domain.Common;

namespace Atlas.Domain.Organization;

/// <summary>
/// An organizational unit whose members share the department's data space. A department is fully functional with no head and
/// with no members. Membership itself is stored on the user (<c>User.DepartmentId</c>), which is what makes it exclusive:
/// a user has at most one department.
/// </summary>
public sealed class Department
{
    private Department() { } // EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string? Description { get; private set; }

    /// <summary>Optional. When set, this user must be a member of the department (enforced by the use cases).</summary>
    public Guid? HeadUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Department Create(string name, string? description, DateTimeOffset now)
    {
        var (clean, desc) = Validate(name, description);
        return new Department
        {
            Id = Guid.CreateVersion7(),
            Name = clean,
            NormalizedName = OrganizationNamePolicy.Normalize(clean),
            Description = desc,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(string name, string? description, DateTimeOffset now)
    {
        var (clean, desc) = Validate(name, description);
        Name = clean;
        NormalizedName = OrganizationNamePolicy.Normalize(clean);
        Description = desc;
        UpdatedAt = now;
    }

    public bool IsHead(Guid userId) => HeadUserId == userId;

    /// <summary>Designates (or replaces) the head. The caller guarantees the user is a member of this department.</summary>
    public void AppointHead(Guid userId, DateTimeOffset now)
    {
        if (HeadUserId == userId) return;
        HeadUserId = userId;
        UpdatedAt = now;
    }

    /// <summary>Leaves the department without a head. No replacement is required.</summary>
    public void ClearHead(DateTimeOffset now)
    {
        if (HeadUserId is null) return;
        HeadUserId = null;
        UpdatedAt = now;
    }

    private static (string Name, string? Description) Validate(string name, string? description)
    {
        var errors = OrganizationNamePolicy.ValidateName(name).Concat(OrganizationNamePolicy.ValidateDescription(description)).ToList();
        if (errors.Count > 0)
            throw new DomainException("invalid_department", string.Join(" ", errors));
        return (OrganizationNamePolicy.Clean(name), string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }
}
