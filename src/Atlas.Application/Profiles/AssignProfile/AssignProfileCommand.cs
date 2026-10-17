using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Profiles.AssignProfile;

/// <summary>
/// Gives the user a profile (assigning or replacing). Independent of department membership: the user does not need a
/// department to hold a profile, and assigning one never changes the department. The replaced profile is retained.
/// </summary>
public sealed record AssignProfileCommand(Guid UserId, Guid ProfileId) : IRequest<Result<Users.UserDto>>, IRequireAdmin;

public sealed class AssignProfileCommandValidator : AbstractValidator<AssignProfileCommand>
{
    public AssignProfileCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ProfileId).NotEmpty();
    }
}

public sealed class AssignProfileCommandHandler(
    IUserRepository users,
    IProfileRepository profiles,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<AssignProfileCommandHandler> logger) : IRequestHandler<AssignProfileCommand, Result<Users.UserDto>>
{
    public Task<Result<Users.UserDto>> Handle(AssignProfileCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<Users.UserDto>>(async ct =>
        {
            var user = await users.GetByIdAsync(request.UserId, ct);
            if (user is null) return Error.NotFound("User not found.");

            var profile = await profiles.GetByIdAsync(request.ProfileId, ct);
            if (profile is null) return Error.NotFound("Profile not found.");

            if (user.Role == UserRole.Admin)
                return Error.BusinessRule("admin_not_assignable",
                    "Administrator accounts are outside the organization and cannot receive a profile.");

            var previous = user.ProfileId;
            user.AssignProfile(profile.Id, clock.GetUtcNow());
            await users.SaveChangesAsync(ct);

            logger.LogInformation("Admin {ActorId} assigned profile {ProfileId} to user {UserId} (previous profile {PreviousProfileId})",
                currentUser.UserId, profile.Id, user.Id, previous);
            return user.ToDto();
        }, cancellationToken);
}
