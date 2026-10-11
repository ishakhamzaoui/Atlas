using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<LoginResponse>;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator() => RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}

public sealed class RefreshTokenCommandHandler(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IRefreshTokenProvider provider,
    SessionIssuer issuer,
    TimeProvider clock,
    ILogger<RefreshTokenCommandHandler> logger) : IRequestHandler<RefreshTokenCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var stored = await refreshTokens.GetByHashAsync(provider.Hash(request.RefreshToken), cancellationToken);
        if (stored is null)
        {
            logger.LogWarning("Refresh rejected: unknown token");
            throw Invalid();
        }

        if (stored.IsRevoked)
        {
            // A rotated/revoked token came back: either a replay or a stolen copy. Kill the whole session family.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking session family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            throw Invalid();
        }

        if (stored.IsExpired(now))
        {
            logger.LogDebug("Refresh rejected: token {TokenId} expired at {ExpiresAt}", stored.Id, stored.ExpiresAt);
            throw Invalid();
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.MustChangePassword || user.SecurityStamp != stored.SecurityStamp)
        {
            logger.LogInformation("Refresh rejected for user {UserId}: account unavailable or session invalidated; revoking family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            throw Invalid();
        }

        // Rotate: issue the successor and retire the presented token in ONE save (optimistic concurrency on the old row).
        var session = issuer.Issue(user, stored);
        stored.Revoke(now, session.RefreshToken!.Id);

        try
        {
            await refreshTokens.SaveChangesAsync(cancellationToken);
        }
        catch (ConflictException)
        {
            // Two requests presented the same token at once; treat the loser as a replay.
            logger.LogWarning("Concurrent use of refresh token for user {UserId}; revoking family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            throw Invalid();
        }

        logger.LogDebug("Refresh token rotated for user {UserId} (family {FamilyId})", user.Id, stored.FamilyId);
        return session.Response;
    }

    private static UnauthorizedException Invalid() =>
        new("invalid_refresh_token", "The refresh token is invalid or expired.");
}
