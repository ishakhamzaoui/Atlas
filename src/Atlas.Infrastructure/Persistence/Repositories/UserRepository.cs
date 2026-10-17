using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atlas.Infrastructure.Persistence.Repositories;

public sealed class UserRepository(AtlasDbContext db) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalizedUsername, cancellationToken);

    public Task<bool> UsernameExistsAsync(string normalizedUsername, Guid? excludingUserId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(
            u => u.NormalizedUsername == normalizedUsername && (excludingUserId == null || u.Id != excludingUserId),
            cancellationToken);

    public Task<bool> AnyAdminAsync(CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(u => u.Role == UserRole.Admin, cancellationToken);

    public Task<int> CountActiveAdminsAsync(Guid excludingUserId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().CountAsync(
            u => u.Role == UserRole.Admin && u.IsActive && u.Id != excludingUserId, cancellationToken);

    public async Task<PagedResult<User>> ListAsync(UserListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<User> query = db.Users.AsNoTracking();

        if (filter.Role is { } role) query = query.Where(u => u.Role == role);
        if (filter.IsActive is { } active) query = query.Where(u => u.IsActive == active);
        if (filter.DepartmentId is { } departmentId) query = query.Where(u => u.DepartmentId == departmentId);
        if (filter.ProfileId is { } profileId) query = query.Where(u => u.ProfileId == profileId);
        if (filter.Orphan is { } orphan)
        {
            query = orphan
                ? query.Where(u => u.Role == UserRole.User && u.DepartmentId == null && u.ProfileId == null)
                : query.Where(u => u.DepartmentId != null || u.ProfileId != null);
        }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Parameterised substring match on the normalised column; no wildcard injection, no raw SQL.
            var search = UsernamePolicy.Normalize(filter.Search);
            query = query.Where(u => u.NormalizedUsername.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(u => u.NormalizedUsername)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<User>(items, filter.Page, filter.PageSize, total);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        db.Users.Add(user);
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            db.Entry(user).State = EntityState.Detached;
            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => SaveAsync(cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PersistenceConflictException(PersistenceConflictKind.ConcurrentUpdate);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new PersistenceConflictException(PersistenceConflictKind.DuplicateUsername);
        }
    }
}
