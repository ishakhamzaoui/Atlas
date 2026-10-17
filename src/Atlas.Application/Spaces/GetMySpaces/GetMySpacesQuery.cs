using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using Atlas.Domain.Spaces;
using MediatR;

namespace Atlas.Application.Spaces.GetMySpaces;

/// <summary>
/// Lists the spaces that exist for the caller (own private space, own department's space if any, the public space) together with
/// the caller's effective access to each. Evaluated from the CURRENT database state on every call, never from the token, so an
/// administrative change (membership, profile) takes effect immediately.
/// </summary>
public sealed record GetMySpacesQuery : IRequest<Result<MySpacesDto>>;

public sealed class GetMySpacesQueryHandler(
    IUserRepository users,
    IDepartmentRepository departments,
    ICurrentUser currentUser) : IRequestHandler<GetMySpacesQuery, Result<MySpacesDto>>
{
    public async Task<Result<MySpacesDto>> Handle(GetMySpacesQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthenticated();

        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
            return Error.Unauthenticated();

        var privateSpace = new SpaceDto(nameof(DataSpaceKind.Private), user.Id, null, Exists: true,
            SpaceAccessPolicy.Evaluate(user, DataSpace.PrivateOf(user.Id)).ToNames());

        SpaceDto? departmentSpace = null;
        var isHead = false;
        if (user.DepartmentId is { } departmentId)
        {
            var department = await departments.GetByIdAsync(departmentId, cancellationToken);
            if (department is not null)
            {
                isHead = department.IsHead(user.Id);
                departmentSpace = new SpaceDto(nameof(DataSpaceKind.Department), null, department.Id, Exists: true,
                    SpaceAccessPolicy.Evaluate(user, DataSpace.DepartmentOf(department.Id)).ToNames());
            }
        }

        var publicSpace = new SpaceDto(nameof(DataSpaceKind.Public), null, null, Exists: true,
            SpaceAccessPolicy.Evaluate(user, DataSpace.Public).ToNames());

        return new MySpacesDto(user.Id, user.IsOrphan, isHead, user.DepartmentId, user.ProfileId,
            privateSpace, departmentSpace, publicSpace);
    }
}
