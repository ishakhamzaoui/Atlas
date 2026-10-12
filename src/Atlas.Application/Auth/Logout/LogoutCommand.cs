using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Auth.Logout;

/// <summary>Ends the session family the refresh token belongs to. Always succeeds (no token oracle).</summary>
public sealed record LogoutCommand(string RefreshToken) : IRequest<Result>;

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}

public sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenProvider provider,
    TimeProvider clock,
    ILogger<LogoutCommandHandler> logger) : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var stored = await refreshTokens.GetByHashAsync(provider.Hash(request.RefreshToken), cancellationToken);
        if (stored is null)
        {
            logger.LogDebug("Logout with unknown refresh token ignored");
            return Result.Success();
        }

        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, clock.GetUtcNow(), cancellationToken);
        logger.LogInformation("User {UserId} logged out (session family {FamilyId} revoked)", stored.UserId, stored.FamilyId);
        return Result.Success();
    }
}
