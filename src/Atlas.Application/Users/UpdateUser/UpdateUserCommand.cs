using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.UpdateUser;

/// <summary>Full update of the supported properties (username and role). Status and password have dedicated use cases.</summary>
public sealed record UpdateUserCommand(Guid Id, string Username, UserRole Role) : IRequest<UserDto>, IRequireAdmin;

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
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<UpdateUserCommand, UserDto>
{
    public Task<UserDto> Handle(UpdateUserCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync(async ct =>
        {
            var user = await users.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("User not found.");
            var now = clock.GetUtcNow();

            // ---- all checks first, mutations afterwards ----
            var roleChanges = request.Role != user.Role;
            if (roleChanges)
            {
                if (currentUser.UserId == user.Id)
                    throw new BusinessRuleException("cannot_change_own_role", "You cannot change your own role.");

                if (user.Role == UserRole.Admin && user.IsActive &&
                    await users.CountActiveAdminsAsync(user.Id, ct) == 0)
                    throw new BusinessRuleException("last_active_admin", "The last active administrator cannot be demoted.");
            }

            var newUsername = request.Username.Trim();
            var renames = !string.Equals(user.Username, newUsername, StringComparison.Ordinal);
            if (renames && UsernamePolicy.Normalize(newUsername) != user.NormalizedUsername &&
                await users.UsernameExistsAsync(UsernamePolicy.Normalize(newUsername), user.Id, ct))
                throw new ConflictException("username_taken", "That username is already in use.");

            if (roleChanges) user.ChangeRole(request.Role, now);
            if (renames) user.Rename(newUsername, now);

            await users.SaveChangesAsync(ct);
            return user.ToDto();
        }, cancellationToken);
}
