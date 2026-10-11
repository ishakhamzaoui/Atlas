using Atlas.Application.Common.Abstractions;
using Atlas.Application.Users;
using Atlas.Domain.Auth;
using Atlas.Domain.Users;

namespace Atlas.Application.Auth;

public sealed record IssuedSession(LoginResponse Response, RefreshToken? RefreshToken);

/// <summary>
/// Creates an access token and (unless a password change is pending) a refresh token.
/// Does not save: the caller persists the staged refresh token together with its own changes.
/// </summary>
public sealed class SessionIssuer(
    ITokenService tokens,
    IRefreshTokenProvider refreshProvider,
    IRefreshTokenRepository refreshTokens,
    TimeProvider clock)
{
    /// <param name="user">The authenticated user.</param>
    /// <param name="rotatedFrom">The refresh token being replaced; keeps the family and its absolute expiry.</param>
    public IssuedSession Issue(User user, RefreshToken? rotatedFrom = null)
    {
        var now = clock.GetUtcNow();
        var access = tokens.Create(user);

        string? plainRefresh = null;
        RefreshToken? entity = null;

        if (!user.MustChangePassword)
        {
            var generated = refreshProvider.Generate();
            entity = RefreshToken.Issue(
                user.Id,
                generated.Hash,
                rotatedFrom?.FamilyId ?? Guid.NewGuid(),
                user.SecurityStamp,
                now,
                refreshProvider.Lifetime,
                rotatedFrom?.AbsoluteExpiresAt ?? now + refreshProvider.AbsoluteLifetime);

            refreshTokens.Add(entity);
            plainRefresh = generated.Token;
        }

        var response = new LoginResponse(
            access.Value,
            "Bearer",
            Math.Max(0, (int)(access.ExpiresAt - now).TotalSeconds),
            access.ExpiresAt,
            user.MustChangePassword,
            user.ToDto(),
            plainRefresh,
            entity?.ExpiresAt);

        return new IssuedSession(response, entity);
    }
}
