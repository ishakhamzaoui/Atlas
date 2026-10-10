using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.SetUserStatus;

public sealed record SetUserStatusCommand(Guid Id, bool IsActive) : IRequest<UserDto>, IRequireAdmin;

public sealed class SetUserStatusCommandValidator : AbstractValidator<SetUserStatusCommand>
{
    public SetUserStatusCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class SetUserStatusCommandHandler(
    IUserRepository users,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock) : IRequestHandler<SetUserStatusCommand, UserDto>
{
    public Task<UserDto> Handle(SetUserStatusCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync(async ct =>
        {
            var user = await users.GetByIdAsync(request.Id, ct) ?? throw new NotFoundException("User not found.");

            if (!request.IsActive && user.IsActive)
            {
                if (currentUser.UserId == user.Id)
                    throw new BusinessRuleException("cannot_deactivate_self", "You cannot deactivate your own account.");

                if (user.Role == UserRole.Admin && await users.CountActiveAdminsAsync(user.Id, ct) == 0)
                    throw new BusinessRuleException("last_active_admin", "The last active administrator cannot be deactivated.");
            }

            var now = clock.GetUtcNow();
            if (request.IsActive) user.Activate(now); else user.Deactivate(now);

            await users.SaveChangesAsync(ct);
            return user.ToDto();
        }, cancellationToken);
}
