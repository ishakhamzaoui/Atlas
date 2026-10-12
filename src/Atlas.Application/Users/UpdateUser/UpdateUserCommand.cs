using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Users.UpdateUser;

/// <summary>Full update of the supported properties (username and role). Status and password have dedicated use cases.</summary>
public sealed record UpdateUserCommand(Guid Id, string Username, UserRole Role) : IRequest<Result<UserDto>>, IRequireAdmin;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Username).MustBeValidUsername();
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class UpdateUserCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<UpdateUserCommandHandler> logger) : IRequestHandler<UpdateUserCommand, Result<UserDto>>
{
    public Task<Result<UserDto>> Handle(UpdateUserCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<UserDto>>(async ct =>
        {
            var user = await users.GetByIdAsync(request.Id, ct);
            if (user is null)
                return Error.NotFound("User not found.");

            var now = clock.GetUtcNow();

            // ---- all checks first, mutations afterwards ----
            var roleChanges = request.Role != user.Role;
            if (roleChanges)
            {
                if (currentUser.UserId == user.Id)
                    return Error.BusinessRule("cannot_change_own_role", "You cannot change your own role.");

                if (user.Role == UserRole.Admin && user.IsActive &&
                    await users.CountActiveAdminsAsync(user.Id, ct) == 0)
                    return Error.BusinessRule("last_active_admin", "The last active administrator cannot be demoted.");
            }

            var newUsername = request.Username.Trim();
            var renames = !string.Equals(user.Username, newUsername, StringComparison.Ordinal);
            if (renames && UsernamePolicy.Normalize(newUsername) != user.NormalizedUsername &&
                await users.UsernameExistsAsync(UsernamePolicy.Normalize(newUsername), user.Id, ct))
                return Error.Conflict("username_taken", "That username is already in use.");

            if (roleChanges) user.ChangeRole(request.Role, now);
            if (renames) user.Rename(newUsername, now);

            // Role/username are embedded in tokens, and the stamp rotated: end the user's refreshable sessions too.
            if (roleChanges || renames) await refreshTokens.RevokeAllForUserAsync(user.Id, now, ct);

            try
            {
                await users.SaveChangesAsync(ct);
            }
            catch (PersistenceConflictException ex)
            {
                return PersistenceErrors.From(ex);
            }

            logger.LogInformation("Admin {ActorId} updated user {UserId} (roleChanged: {RoleChanged}, renamed: {Renamed})",
                currentUser.UserId, user.Id, roleChanges, renames);
            return user.ToDto();
        }, cancellationToken);
}
