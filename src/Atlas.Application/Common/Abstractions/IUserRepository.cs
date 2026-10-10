using Atlas.Domain.Users;

namespace Atlas.Application.Common.Abstractions;

public sealed record UserListFilter(int Page, int PageSize, string? Search, UserRole? Role, bool? IsActive);

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken);
    Task<bool> UsernameExistsAsync(string normalizedUsername, Guid? excludingUserId, CancellationToken cancellationToken);
    Task<bool> AnyAdminAsync(CancellationToken cancellationToken);
    Task<int> CountActiveAdminsAsync(Guid excludingUserId, CancellationToken cancellationToken);
    Task<PagedResult<User>> ListAsync(UserListFilter filter, CancellationToken cancellationToken);

    /// <summary>Persists a new user. Throws <c>ConflictException("username_taken")</c> on a duplicate username.</summary>
    Task AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>Persists tracked changes. Maps unique and concurrency violations to <c>ConflictException</c>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
