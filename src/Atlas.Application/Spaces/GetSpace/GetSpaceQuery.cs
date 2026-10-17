using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using Atlas.Domain.Spaces;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Spaces.GetSpace;

/// <summary>
/// Authorization gate for one specific space: returns the space when the caller may read it, otherwise Forbidden.
/// Access is evaluated BEFORE anything about the space is revealed, so probing ids tells an unauthorized caller nothing.
/// This is the enforcement point later data-management features build on.
/// </summary>
/// <param name="OwnerId">Required for Private (the owner's user id) and Department (the department id); ignored for Public.</param>
public sealed record GetSpaceQuery(DataSpaceKind Kind, Guid? OwnerId) : IRequest<Result<SpaceDto>>;

public sealed class GetSpaceQueryValidator : AbstractValidator<GetSpaceQuery>
{
    public GetSpaceQueryValidator()
    {
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.OwnerId).NotNull().NotEqual(Guid.Empty).When(x => x.Kind != DataSpaceKind.Public);
    }
}

public sealed class GetSpaceQueryHandler(
    IUserRepository users,
    ICurrentUser currentUser,
    ILogger<GetSpaceQueryHandler> logger) : IRequestHandler<GetSpaceQuery, Result<SpaceDto>>
{
    public async Task<Result<SpaceDto>> Handle(GetSpaceQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthenticated();

        var actor = await users.GetByIdAsync(userId, cancellationToken);
        if (actor is null || !actor.IsActive)
            return Error.Unauthenticated();

        var space = request.Kind switch
        {
            DataSpaceKind.Public => DataSpace.Public,
            DataSpaceKind.Private => DataSpace.PrivateOf(request.OwnerId!.Value),
            _ => DataSpace.DepartmentOf(request.OwnerId!.Value)
        };

        var access = SpaceAccessPolicy.Evaluate(actor, space);
        if (access == SpaceAccess.None)
        {
            logger.LogWarning("User {UserId} denied access to {SpaceKind} space {SpaceOwnerId}", actor.Id, request.Kind, request.OwnerId);
            return Error.Forbidden("You do not have access to this space.");
        }

        // Only reachable for spaces the caller is entitled to, and those always exist (own private space, own department's space, public).
        return new SpaceDto(
            request.Kind.ToString(),
            request.Kind == DataSpaceKind.Private ? space.OwnerId : null,
            request.Kind == DataSpaceKind.Department ? space.OwnerId : null,
            Exists: true,
            access.ToNames());
    }
}
