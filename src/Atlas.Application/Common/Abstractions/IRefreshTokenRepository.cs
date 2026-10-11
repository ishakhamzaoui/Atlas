using Atlas.Domain.Auth;

namespace Atlas.Application.Common.Abstractions;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>Stages a new token; persisted by the next SaveChanges of the same unit of work.</summary>
    void Add(RefreshToken token);

    /// <summary>Immediately revokes every still-active token of the family.</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Immediately revokes every still-active token of the user (all devices).</summary>
    Task RevokeAllForUserAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Persists staged changes. Maps optimistic-concurrency failures to <c>ConflictException</c>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
