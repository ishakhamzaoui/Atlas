using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken) : IRequest<Result<LoginResponse>>;

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
    ILogger<RefreshTokenCommandHandler> logger) : IRequestHandler<RefreshTokenCommand, Result<LoginResponse>>
{
    private static readonly Error Invalid =
        Error.Unauthorized("invalid_refresh_token", "The refresh token is invalid or expired.");

    public async Task<Result<LoginResponse>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var stored = await refreshTokens.GetByHashAsync(provider.Hash(request.RefreshToken), cancellationToken);
        if (stored is null)
        {
            logger.LogWarning("Refresh rejected: unknown token");
            return Invalid;
        }

        if (stored.IsRevoked)
        {
            // A rotated/revoked token came back: either a replay or a stolen copy. Kill the whole session family.
            logger.LogWarning("Refresh token reuse detected for user {UserId}; revoking session family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return Invalid;
        }

        if (stored.IsExpired(now))
        {
            logger.LogDebug("Refresh rejected: token {TokenId} expired at {ExpiresAt}", stored.Id, stored.ExpiresAt);
            return Invalid;
        }

        var user = await users.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive || user.MustChangePassword || user.SecurityStamp != stored.SecurityStamp)
        {
            logger.LogInformation("Refresh rejected for user {UserId}: account unavailable or session invalidated; revoking family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return Invalid;
        }

        // Rotate: issue the successor and retire the presented token in ONE save (optimistic concurrency on the old row).
        var session = issuer.Issue(user, stored);
        stored.Revoke(now, session.RefreshToken!.Id);

        try
        {
            await refreshTokens.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            // Two requests presented the same token at once; treat the loser as a replay.
            logger.LogWarning("Concurrent use of refresh token for user {UserId}; revoking family {FamilyId}",
                stored.UserId, stored.FamilyId);
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return Invalid;
        }

        logger.LogDebug("Refresh token rotated for user {UserId} (family {FamilyId})", user.Id, stored.FamilyId);
        return session.Response;
    }
}
