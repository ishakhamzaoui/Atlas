using Atlas.Domain.Organization;

namespace Atlas.Application.Common.Abstractions;

public sealed record ProfileSummary(Profile Profile, int AssignedUserCount);

/// <param name="Assigned">true: only profiles held by at least one user; false: only unassigned (retained) profiles.</param>
public sealed record ProfileListFilter(int Page, int PageSize, string? Search, bool? Assigned);

public interface IProfileRepository
{
    Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<ProfileSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string normalizedName, Guid? excludingProfileId, CancellationToken cancellationToken);
    Task<PagedResult<ProfileSummary>> ListAsync(ProfileListFilter filter, CancellationToken cancellationToken);

    /// <summary>Persists a new profile. Throws <c>PersistenceConflictException(DuplicateProfileName)</c> on a duplicate name.</summary>
    Task AddAsync(Profile profile, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
