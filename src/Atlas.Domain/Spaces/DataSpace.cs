namespace Atlas.Domain.Spaces;

public enum DataSpaceKind
{
    /// <summary>One per user account; owned by that user.</summary>
    Private,

    /// <summary>One per department; shared by its members.</summary>
    Department,

    /// <summary>One for the whole platform; readable by every authenticated user.</summary>
    Public
}

/// <summary>
/// Access a user has to a space. <see cref="Write"/> exists so later phases can grant it, but nothing grants it yet:
/// until the data-management phase defines write rules the platform fails closed.
/// </summary>
[Flags]
public enum SpaceAccess
{
    None = 0,
    Read = 1,
    Write = 2
}

/// <summary>
/// The identity of a logical data space. Spaces always exist (they are derived from the user / department / platform itself),
/// independent of heads, profiles or memberships; what varies is who is AUTHORIZED to use them.
/// </summary>
public readonly record struct DataSpace(DataSpaceKind Kind, Guid? OwnerId)
{
    public static DataSpace Public { get; } = new(DataSpaceKind.Public, null);
    public static DataSpace PrivateOf(Guid userId) => new(DataSpaceKind.Private, userId);
    public static DataSpace DepartmentOf(Guid departmentId) => new(DataSpaceKind.Department, departmentId);
}
