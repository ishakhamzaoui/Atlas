using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Auth.Login;

public sealed record LoginCommand(string Username, string Password) : IRequest<LoginResponse>;

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
    IPasswordHasher hasher,
    ITokenService tokens,
    TimeProvider clock) : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await users.GetByUsernameAsync(UsernamePolicy.Normalize(request.Username), cancellationToken);

        // Always verify (against a dummy hash for unknown users) so timing does not reveal whether the username exists,
        // and check IsActive only afterwards so inactive accounts are indistinguishable from wrong passwords.
        var passwordMatches = hasher.Verify(request.Password, user?.PasswordHash ?? hasher.DummyHash);

        if (user is null || !passwordMatches || !user.IsActive)
            throw new UnauthorizedException();

        return LoginResponse.From(user, tokens.Create(user), clock.GetUtcNow());
    }
}
