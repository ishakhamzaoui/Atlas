using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Application.Users;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Organization.RemoveUserFromDepartment;

/// <summary>
/// Takes the user out of their department without touching the account. Explicit side effects: the department head role
/// (if held) ends and the profile association is removed so the user is left with no organizational access (an orphan).
/// The profile itself is retained. Idempotent for users who are in no department.
/// </summary>
public sealed record RemoveUserFromDepartmentCommand(Guid UserId) : IRequest<Result<OrganizationChangeDto>>, IRequireAdmin;

public sealed class RemoveUserFromDepartmentCommandValidator : AbstractValidator<RemoveUserFromDepartmentCommand>
{
    public RemoveUserFromDepartmentCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class RemoveUserFromDepartmentCommandHandler(
    IUserRepository users,
    IDepartmentRepository departments,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<RemoveUserFromDepartmentCommandHandler> logger) : IRequestHandler<RemoveUserFromDepartmentCommand, Result<OrganizationChangeDto>>
{
    public Task<Result<OrganizationChangeDto>> Handle(RemoveUserFromDepartmentCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<OrganizationChangeDto>>(async ct =>
        {
            var user = await users.GetByIdAsync(request.UserId, ct);
            if (user is null) return Error.NotFound("User not found.");

            if (user.DepartmentId is not { } departmentId)
                return new OrganizationChangeDto(user.ToDto(), null, null, false);

            var now = clock.GetUtcNow();
            var wasHead = false;
            var department = await departments.GetByIdAsync(departmentId, ct);
            if (department is not null && department.IsHead(user.Id))
            {
                department.ClearHead(now);
                wasHead = true;
            }

            var removedProfile = user.ProfileId;
            user.RemoveProfile(now);
            user.RemoveFromDepartment(now);
            await users.SaveChangesAsync(ct);

            logger.LogInformation("Admin {ActorId} removed user {UserId} from department {DepartmentId} (wasHead {WasHead}, profile {ProfileId} detached and retained)",
                currentUser.UserId, user.Id, departmentId, wasHead, removedProfile);
            return new OrganizationChangeDto(user.ToDto(), departmentId, removedProfile, wasHead);
        }, cancellationToken);
}
