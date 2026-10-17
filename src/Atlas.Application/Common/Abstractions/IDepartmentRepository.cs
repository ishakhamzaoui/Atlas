using Atlas.Domain.Organization;

namespace Atlas.Application.Common.Abstractions;

public sealed record DepartmentSummary(Department Department, int MemberCount, string? HeadUsername);

public sealed record DepartmentListFilter(int Page, int PageSize, string? Search);

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<DepartmentSummary?> GetSummaryAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(string normalizedName, Guid? excludingDepartmentId, CancellationToken cancellationToken);
    Task<PagedResult<DepartmentSummary>> ListAsync(DepartmentListFilter filter, CancellationToken cancellationToken);

    /// <summary>Persists a new department. Throws <c>PersistenceConflictException(DuplicateDepartmentName)</c> on a duplicate name.</summary>
    Task AddAsync(Department department, CancellationToken cancellationToken);

    /// <summary>Persists tracked changes (shared unit of work with the user repository).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
