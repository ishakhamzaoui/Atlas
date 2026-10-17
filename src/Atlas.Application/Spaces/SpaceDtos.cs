using Atlas.Domain.Spaces;

namespace Atlas.Application.Spaces;

/// <summary>
/// A data space as seen by one caller. <see cref="Exists"/> and <see cref="Access"/> are deliberately separate:
/// a space can exist while the caller is not authorized to use it.
/// </summary>
/// <param name="Kind">Private, Department or Public.</param>
/// <param name="OwnerUserId">The owner for a private space.</param>
/// <param name="DepartmentId">The owning department for a department space.</param>
/// <param name="Exists">Always true for the caller's own private space, any department's space and the public space.</param>
/// <param name="Access">What the caller may do: empty (no access) or "Read". "Write" is never granted in this phase.</param>
public sealed record SpaceDto(
    string Kind,
    Guid? OwnerUserId,
    Guid? DepartmentId,
    bool Exists,
    IReadOnlyList<string> Access)
{
    public bool CanRead => Access.Contains(nameof(SpaceAccess.Read));
}

/// <summary>The caller's spaces and the organizational state that drives their access.</summary>
public sealed record MySpacesDto(
    Guid UserId,
    bool IsOrphan,
    bool IsDepartmentHead,
    Guid? DepartmentId,
    Guid? ProfileId,
    SpaceDto Private,
    SpaceDto? Department,
    SpaceDto Public);

internal static class SpaceMappings
{
    public static IReadOnlyList<string> ToNames(this SpaceAccess access)
    {
        var names = new List<string>();
        if (access.HasFlag(SpaceAccess.Read)) names.Add(nameof(SpaceAccess.Read));
        if (access.HasFlag(SpaceAccess.Write)) names.Add(nameof(SpaceAccess.Write));
        return names;
    }
}
