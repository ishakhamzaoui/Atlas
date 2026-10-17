using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Departments.ListDepartments;

public sealed record ListDepartmentsQuery(int Page, int PageSize, string? Search)
    : IRequest<Result<PagedResult<DepartmentDto>>>, IRequireAdmin;

public sealed class ListDepartmentsQueryValidator : AbstractValidator<ListDepartmentsQuery>
{
    public const int MaxPageSize = 100;

    public ListDepartmentsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(OrganizationNamePolicy.MaxLength);
    }
}

public sealed class ListDepartmentsQueryHandler(IDepartmentRepository departments)
    : IRequestHandler<ListDepartmentsQuery, Result<PagedResult<DepartmentDto>>>
{
    public async Task<Result<PagedResult<DepartmentDto>>> Handle(ListDepartmentsQuery request, CancellationToken cancellationToken)
    {
        var page = await departments.ListAsync(new DepartmentListFilter(request.Page, request.PageSize, request.Search), cancellationToken);
        return page.Map(DepartmentDto.From);
    }
}
