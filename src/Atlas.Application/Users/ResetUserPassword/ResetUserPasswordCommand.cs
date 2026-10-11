using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Users.ResetUserPassword;

public sealed record ResetUserPasswordCommand(Guid Id) : IRequest<ResetPasswordResponse>, IRequireAdmin;

/// <param name="TemporaryPassword">Shown exactly once. Communicate it out-of-band; the user must change it at next login.</param>
public sealed record ResetPasswordResponse(string TemporaryPassword, bool MustChangePassword);

public sealed class ResetUserPasswordCommandValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class ResetUserPasswordCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    IPasswordGenerator generator,
    TimeProvider clock,
    ILogger<ResetUserPasswordCommandHandler> logger) : IRequestHandler<ResetUserPasswordCommand, ResetPasswordResponse>
{
    public async Task<ResetPasswordResponse> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == request.Id)
            throw new BusinessRuleException("cannot_reset_own_password",
                "Use the change-password endpoint to change your own password.");

        var user = await users.GetByIdAsync(request.Id, cancellationToken)
                   ?? throw new NotFoundException("User not found.");

        var temporaryPassword = generator.Generate();
        var now = clock.GetUtcNow();
        user.SetTemporaryPassword(hasher.Hash(temporaryPassword), now);
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, cancellationToken);
        await users.SaveChangesAsync(cancellationToken);
        logger.LogWarning("Admin {ActorId} reset the password of user {UserId}; sessions revoked", currentUser.UserId, user.Id);

        return new ResetPasswordResponse(temporaryPassword, MustChangePassword: true);
    }
}
