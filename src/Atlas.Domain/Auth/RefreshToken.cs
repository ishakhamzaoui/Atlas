using Atlas.Domain.Common;

namespace Atlas.Domain.Auth;

/// <summary>
/// Server-side record of an opaque refresh token. Only the SHA-256 hash of the token is stored.
/// Tokens are single-use (rotation) and grouped into a family that is revoked together on reuse.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken() { } // EF Core

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = default!;

    /// <summary>All tokens descended from one login share a family (used for theft detection).</summary>
    public Guid FamilyId { get; private set; }

    /// <summary>The user's security stamp when issued; a mismatch later means the session was invalidated.</summary>
    public Guid SecurityStamp { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Hard cap for the whole family; rotation never extends past it.</summary>
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsExpired(DateTimeOffset now) => ExpiresAt <= now;

    public static RefreshToken Issue(
        Guid userId, string tokenHash, Guid familyId, Guid securityStamp,
        DateTimeOffset now, TimeSpan lifetime, DateTimeOffset absoluteExpiresAt)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("invalid_refresh_token", "A token hash is required.");

        var expires = now + lifetime;
        if (expires > absoluteExpiresAt) expires = absoluteExpiresAt;
        if (expires <= now)
            throw new DomainException("refresh_token_expired", "The session has reached its absolute lifetime.");

        return new RefreshToken
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            SecurityStamp = securityStamp,
            CreatedAt = now,
            ExpiresAt = expires,
            AbsoluteExpiresAt = absoluteExpiresAt
        };
    }

    public void Revoke(DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null) return;
        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }
}
