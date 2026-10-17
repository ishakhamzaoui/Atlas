using Atlas.Domain.Users;

namespace Atlas.Domain.Spaces;

/// <summary>
/// The CURRENT (minimal) authorization rules for data spaces. Pure and side-effect free; the data-management phase refines
/// this single class (item ownership, department-head privileges, write rules) without touching callers.
///
/// Rules today:
///  * Public space: every active authenticated account may read it (organizational state is irrelevant, orphans included).
///  * Private space: only its owner, and only while the owner holds organizational privileges (department AND profile).
///  * Department space: only members of that department who hold a profile.
///  * Administrator accounts are outside the organization: they only read the public space.
///  * Nothing grants Write yet.
/// </summary>
public static class SpaceAccessPolicy
{
    public static SpaceAccess Evaluate(User actor, DataSpace space)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsActive) return SpaceAccess.None;

        return space.Kind switch
        {
            DataSpaceKind.Public => SpaceAccess.Read,

            DataSpaceKind.Private when actor.HasOrganizationalPrivileges && space.OwnerId == actor.Id
                => SpaceAccess.Read,

            DataSpaceKind.Department when actor.HasOrganizationalPrivileges && space.OwnerId == actor.DepartmentId
                => SpaceAccess.Read,

            _ => SpaceAccess.None
        };
    }
}
