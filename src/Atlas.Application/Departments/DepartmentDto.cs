using Atlas.Application.Common.Abstractions;
using Atlas.Application.Users;

namespace Atlas.Application.Departments;

/// <param name="HeadUserId">Null while the department has no head (a perfectly valid state).</param>
public sealed record DepartmentDto(
    Guid Id,
    string Name,
    string? Description,
    Guid? HeadUserId,
    string? HeadUsername,
    int MemberCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static DepartmentDto From(DepartmentSummary s) =>
        new(s.Department.Id, s.Department.Name, s.Department.Description, s.Department.HeadUserId, s.HeadUsername,
            s.MemberCount, s.Department.CreatedAt, s.Department.UpdatedAt);
}

public sealed record DepartmentMemberDto(UserDto User, bool IsHead);
