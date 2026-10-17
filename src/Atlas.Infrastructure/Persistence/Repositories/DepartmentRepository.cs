using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atlas.Infrastructure.Persistence.Repositories;

public sealed class DepartmentRepository(AtlasDbContext db) : IDepartmentRepository
{
    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Departments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<DepartmentSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await Summaries(db.Departments.AsNoTracking().Where(d => d.Id == id)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : new DepartmentSummary(row.Department, row.MemberCount, row.HeadUsername);
    }

    public Task<bool> NameExistsAsync(string normalizedName, Guid? excludingDepartmentId, CancellationToken cancellationToken) =>
        db.Departments.AsNoTracking().AnyAsync(
            d => d.NormalizedName == normalizedName && (excludingDepartmentId == null || d.Id != excludingDepartmentId),
            cancellationToken);

    public async Task<PagedResult<DepartmentSummary>> ListAsync(DepartmentListFilter filter, CancellationToken cancellationToken)
    {
        IQueryable<Department> query = db.Departments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = OrganizationNamePolicy.Normalize(filter.Search);
            query = query.Where(d => d.NormalizedName.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await Summaries(query.OrderBy(d => d.NormalizedName).Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize))
            .ToListAsync(cancellationToken);

        return new PagedResult<DepartmentSummary>(
            rows.Select(r => new DepartmentSummary(r.Department, r.MemberCount, r.HeadUsername)).ToList(),
            filter.Page, filter.PageSize, total);
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken)
    {
        db.Departments.Add(department);
        try
        {
            await SaveAsync(cancellationToken);
        }
        catch (PersistenceConflictException)
        {
            db.Entry(department).State = EntityState.Detached;
            throw;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => SaveAsync(cancellationToken);

    private sealed record Row(Department Department, int MemberCount, string? HeadUsername);

    private IQueryable<Row> Summaries(IQueryable<Department> departments) =>
        departments.Select(d => new Row(
            d,
            db.Users.Count(u => u.DepartmentId == d.Id),
            db.Users.Where(u => u.Id == d.HeadUserId).Select(u => u.Username).FirstOrDefault()));

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
            throw new PersistenceConflictException(pg.ConstraintName == "ux_departments_normalized_name"
                ? PersistenceConflictKind.DuplicateDepartmentName
                : PersistenceConflictKind.ConcurrentUpdate);
        }
    }
}
