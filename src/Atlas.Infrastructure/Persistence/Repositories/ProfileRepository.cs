using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atlas.Infrastructure.Persistence.Repositories;

public sealed class ProfileRepository(AtlasDbContext db) : IProfileRepository
{
    public Task<Profile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Profiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<ProfileSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Summaries(db.Profiles.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new ProfileSummary(row.Profile, row.AssignedUserCount);
    }

    public Task<bool> NameExistsAsync(string normalizedName, Guid? excludingProfileId, CancellationToken cancellationToken) =>
        db.Profiles.AsNoTracking().AnyAsync(
            p => p.NormalizedName == normalizedName && (excludingProfileId == null || p.Id != excludingProfileId),
            cancellationToken);

    public async Task<PagedResult<ProfileSummary>> ListAsync(ProfileListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Profile> query = db.Profiles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = OrganizationNamePolicy.Normalize(filter.Search);
            query = query.Where(p => p.NormalizedName.Contains(search));
        }

        if (filter.Assigned is { } assigned)
        {
            query = assigned
                ? query.Where(p => db.Users.Any(u => u.ProfileId == p.Id))
                : query.Where(p => !db.Users.Any(u => u.ProfileId == p.Id));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await Summaries(query.OrderBy(p => p.NormalizedName).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<ProfileSummary>(
            rows.Select(r => new ProfileSummary(r.Profile, r.AssignedUserCount)).ToList(),
            filter.Page, filter.PageSize, total);
    }

    public async Task AddAsync(Profile profile, CancellationToken cancellationToken)
    {
        db.Profiles.Add(profile);
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            db.Entry(profile).State = EntityState.Detached;
            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => SaveAsync(cancellationToken);

    private sealed record Row(Profile Profile, int AssignedUserCount);

    private IQueryable<Row> Summaries(IQueryable<Profile> profiles) =>
        profiles.Select(p => new Row(p, db.Users.Count(u => u.ProfileId == p.Id)));

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
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new PersistenceConflictException(pg.ConstraintName == "ux_profiles_normalized_name"
                ? PersistenceConflictKind.DuplicateProfileName
                : PersistenceConflictKind.ConcurrentUpdate);
        }
    }
}
