using Atlas.Domain.Users;

namespace Atlas.Application.Common.Abstractions;

/// <param name="DepartmentId">Only members of this department.</param>
/// <param name="ProfileId">Only users holding this profile.</param>
/// <param name="Orphan">true: only users with neither department nor profile; false: only users with at least one.</param>
public sealed record UserListFilter(
    int Page, int PageSize, string? Search, UserRole? Role, bool? IsActive,
    Guid? DepartmentId = null, Guid? ProfileId = null, bool? Orphan = null);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken);
    Task<bool> UsernameExistsAsync(string normalizedUsername, Guid? excludingUserId, CancellationToken cancellationToken);
    Task<bool> AnyAdminAsync(CancellationToken cancellationToken);
    Task<int> CountActiveAdminsAsync(Guid excludingUserId, CancellationToken cancellationToken);
    Task<PagedResult<User>> ListAsync(UserListFilter filter, CancellationToken cancellationToken);

    /// <summary>Persists a new user. Throws <c>PersistenceConflictException(DuplicateUsername)</c> on a duplicate username.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>Persists tracked changes. Maps unique and concurrency violations to <c>PersistenceConflictException</c>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
