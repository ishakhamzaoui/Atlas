using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Application.Users;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Organization.AssignUserToDepartment;

/// <summary>
/// Puts a user into a department: an unassigned user joins it, a user of another department is moved (no transfer request or
/// approval needed). Membership is exclusive, so the previous membership always ends.
///
/// Explicit rules for what happens to the rest of the user's organizational state:
///  * Moving from another department: the old department's head role (if any) ends, and the profile association is removed
///    (the profile is retained) so the user keeps no access tied to the former department. Pass <c>ProfileId</c> to give the
///    user a profile in the same operation.
///  * Joining from "no department": an existing profile association is kept unless <c>ProfileId</c> replaces it.
///  * Already in this department: nothing about membership changes; <c>ProfileId</c>, if given, is applied.
/// </summary>
public sealed record AssignUserToDepartmentCommand(Guid UserId, Guid DepartmentId, Guid? ProfileId = null)
    : IRequest<Result<OrganizationChangeDto>>, IRequireAdmin;

public sealed class AssignUserToDepartmentCommandValidator : AbstractValidator<AssignUserToDepartmentCommand>
{
    public AssignUserToDepartmentCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.ProfileId).NotEqual(Guid.Empty).When(x => x.ProfileId.HasValue);
    }
}

public sealed class AssignUserToDepartmentCommandHandler(
    IUserRepository users,
    IDepartmentRepository departments,
    IProfileRepository profiles,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AssignUserToDepartmentCommandHandler> logger) : IRequestHandler<AssignUserToDepartmentCommand, Result<OrganizationChangeDto>>
{
    public Task<Result<OrganizationChangeDto>> Handle(AssignUserToDepartmentCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<OrganizationChangeDto>>(async ct =>
        {
            var user = await users.GetByIdAsync(request.UserId, ct);
            if (user is null) return Error.NotFound("User not found.");

            var target = await departments.GetByIdAsync(request.DepartmentId, ct);
            if (target is null) return Error.NotFound("Department not found.");

            if (request.ProfileId is { } newProfileId && await profiles.GetByIdAsync(newProfileId, ct) is null)
                return Error.NotFound("Profile not found.");

            if (user.Role == UserRole.Admin)
                return Error.BusinessRule("admin_not_assignable",
                    "Administrator accounts are outside the organization and cannot join a department.");

            var now = clock.GetUtcNow();
            var previousDepartmentId = user.DepartmentId;
            var originalProfileId = user.ProfileId;
            var wasHead = false;
            var profileRemoved = false;

            if (previousDepartmentId != target.Id)
            {
                if (previousDepartmentId is { } oldId)
                {
                    // A real move: end leadership of the old department and drop the profile tied to it.
                    var old = await departments.GetByIdAsync(oldId, ct);
                    if (old is not null && old.IsHead(user.Id))
                    {
                        old.ClearHead(now);
                        wasHead = true;
                    }

                    if (user.ProfileId is not null)
                    {
                        user.RemoveProfile(now);
                        profileRemoved = true;
                    }
                }

                user.AssignToDepartment(target.Id, now);
            }

            if (request.ProfileId is { } profileId)
            {
                if (originalProfileId is not null && originalProfileId != profileId) profileRemoved = true;
                user.AssignProfile(profileId, now);
            }

            if (user.ProfileId == originalProfileId) profileRemoved = false; // removed and re-given: net no change

            await users.SaveChangesAsync(ct);

            logger.LogInformation(
                "Admin {ActorId} placed user {UserId} in department {DepartmentId} (previous {PreviousDepartmentId}, wasHead {WasHead}, profileRemoved {ProfileRemoved}, profileNow {ProfileId})",
                currentUser.UserId, user.Id, target.Id, previousDepartmentId, wasHead, profileRemoved, user.ProfileId);

            return new OrganizationChangeDto(user.ToDto(), previousDepartmentId,
                profileRemoved ? originalProfileId : null, wasHead);
        }, cancellationToken);
}
