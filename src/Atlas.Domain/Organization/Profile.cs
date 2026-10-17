using Atlas.Domain.Common;

namespace Atlas.Domain.Organization;

/// <summary>
/// Defines the access privileges a user receives. Kept separate from the user account: it can be assigned, replaced and
/// removed without touching the account, and it is retained (never deleted) when no user holds it any more.
/// This phase only models the profile itself; the concrete privileges are defined in the data-management phase.
/// </summary>
public sealed class Profile
{
    private Profile() { } // EF Core

    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string NormalizedName { get; private set; } = default!;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Profile Create(string name, string? description, DateTimeOffset now)
    {
        var (clean, desc) = Validate(name, description);
        return new Profile
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

    private static (string Name, string? Description) Validate(string name, string? description)
    {
        var errors = OrganizationNamePolicy.ValidateName(name).Concat(OrganizationNamePolicy.ValidateDescription(description)).ToList();
        if (errors.Count > 0)
            throw new DomainException("invalid_profile", string.Join(" ", errors));
        return (OrganizationNamePolicy.Clean(name), string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }
}
