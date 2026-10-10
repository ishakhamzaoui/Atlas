using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using FluentValidation;
using MediatR;

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
    ICurrentUser currentUser,
    IPasswordHasher hasher,
    IPasswordGenerator generator,
    TimeProvider clock) : IRequestHandler<ResetUserPasswordCommand, ResetPasswordResponse>
{
    public async Task<ResetPasswordResponse> Handle(ResetUserPasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId == request.Id)
            throw new BusinessRuleException("cannot_reset_own_password",
                "Use the change-password endpoint to change your own password.");

        var user = await users.GetByIdAsync(request.Id, cancellationToken)
                   ?? throw new NotFoundException("User not found.");

        var temporaryPassword = generator.Generate();
        user.SetTemporaryPassword(hasher.Hash(temporaryPassword), clock.GetUtcNow());
        await users.SaveChangesAsync(cancellationToken);

        return new ResetPasswordResponse(temporaryPassword, MustChangePassword: true);
    }
}
