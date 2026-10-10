using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Validation;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Auth.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string ConfirmNewPassword)
    : IRequest<LoginResponse>;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(256);
        RuleFor(x => x.NewPassword).MustMeetPasswordPolicy();
        RuleFor(x => x.ConfirmNewPassword)
            .Equal(x => x.NewPassword).WithMessage("Password confirmation does not match the new password.");
    }
}

public sealed class ChangePasswordCommandHandler(
    IUserRepository users,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    ITokenService tokens,
    TimeProvider clock) : IRequestHandler<ChangePasswordCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("unauthenticated", "Authentication is required.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException("unauthenticated", "Authentication is required.");

        // Required for everyone, including the forced first-login change (the temporary password is the "current" one).
        if (!hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new BusinessRuleException("invalid_current_password", "The current password is incorrect.");

        if (hasher.Verify(request.NewPassword, user.PasswordHash))
            throw new BusinessRuleException("password_unchanged", "The new password must be different from the current password.");

        var now = clock.GetUtcNow();
        user.ChangePassword(hasher.Hash(request.NewPassword), now);
        await users.SaveChangesAsync(cancellationToken);

        // The security stamp rotated, so earlier tokens are dead; hand back a fresh, unrestricted one.
        return LoginResponse.From(user, tokens.Create(user), now);
    }
}
