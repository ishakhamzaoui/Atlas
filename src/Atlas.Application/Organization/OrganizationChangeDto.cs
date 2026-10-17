using Atlas.Application.Users;

namespace Atlas.Application.Organization;

/// <summary>
/// Result of a membership change, spelling out the side effects explicitly so nothing happens silently.
/// </summary>
/// <param name="PreviousDepartmentId">The department the user belonged to before (null if none).</param>
/// <param name="RemovedProfileId">The profile association that was removed (the profile itself is retained); null if none was removed.</param>
/// <param name="WasDepartmentHead">True if the user headed the previous department; that department now has no head.</param>
public sealed record OrganizationChangeDto(
    UserDto User,
    Guid? PreviousDepartmentId,
    Guid? RemovedProfileId,
    bool WasDepartmentHead);
