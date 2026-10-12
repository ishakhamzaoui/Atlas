using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Auth.Login;

public sealed record LoginCommand(string Username, string Password) : IRequest<Result<LoginResponse>>;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class LoginCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher hasher,
    SessionIssuer issuer,
    ILogger<LoginCommandHandler> logger) : IRequestHandler<LoginCommand, Result<LoginResponse>>
{
    public async Task<Result<LoginResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalized = UsernamePolicy.Normalize(request.Username);
        var user = await users.GetByUsernameAsync(normalized, cancellationToken);

        // Always verify (against a dummy hash for unknown users) so timing does not reveal whether the username exists,
        // and check IsActive only afterwards so inactive accounts are indistinguishable from wrong passwords.
        var passwordMatches = hasher.Verify(request.Password, user?.PasswordHash ?? hasher.DummyHash);

        if (user is null || !passwordMatches || !user.IsActive)
        {
            // The client always gets the same generic 401; the reason stays in the server log only.
            // The attempted username is DEBUG-only: people sometimes type their password into that field.
            var reason = user is null ? "unknown_user" : !passwordMatches ? "wrong_password" : "inactive_account";
            logger.LogWarning("Login failed ({Reason}) for user {UserId}", reason, user?.Id);
            logger.LogDebug("Failed login attempted username {Username}", normalized);
            return Error.Unauthorized();
        }

        var session = issuer.Issue(user);
        await refreshTokens.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User {UserId} logged in (mustChangePassword: {MustChange}, refreshIssued: {RefreshIssued})",
            user.Id, user.MustChangePassword, session.RefreshToken is not null);
        return session.Response;
    }
}
