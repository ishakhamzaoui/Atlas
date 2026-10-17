using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Profiles.RemoveProfile;

/// <summary>Detaches the user's profile. The profile is retained and can be assigned again later. Idempotent.</summary>
public sealed record RemoveProfileCommand(Guid UserId) : IRequest<Result<Users.UserDto>>, IRequireAdmin;

public sealed class RemoveProfileCommandValidator : AbstractValidator<RemoveProfileCommand>
{
    public RemoveProfileCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

public sealed class RemoveProfileCommandHandler(
    IUserRepository users,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<RemoveProfileCommandHandler> logger) : IRequestHandler<RemoveProfileCommand, Result<Users.UserDto>>
{
    public Task<Result<Users.UserDto>> Handle(RemoveProfileCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<Users.UserDto>>(async ct =>
        {
            var user = await users.GetByIdAsync(request.UserId, ct);
            if (user is null) return Error.NotFound("User not found.");

            if (user.ProfileId is not null)
            {
                var removed = user.ProfileId;
                user.RemoveProfile(clock.GetUtcNow());
                await users.SaveChangesAsync(ct);
                logger.LogInformation("Admin {ActorId} removed profile {ProfileId} from user {UserId} (profile retained)",
                    currentUser.UserId, removed, user.Id);
            }

            return user.ToDto();
        }, cancellationToken);
}
