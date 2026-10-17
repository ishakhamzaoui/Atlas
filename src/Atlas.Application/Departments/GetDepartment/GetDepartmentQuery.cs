using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Departments.GetDepartment;

public sealed record GetDepartmentQuery(Guid Id) : IRequest<Result<DepartmentDto>>, IRequireAdmin;

public sealed class GetDepartmentQueryValidator : AbstractValidator<GetDepartmentQuery>
{
    public GetDepartmentQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetDepartmentQueryHandler(IDepartmentRepository departments)
    : IRequestHandler<GetDepartmentQuery, Result<DepartmentDto>>
{
    public async Task<Result<DepartmentDto>> Handle(GetDepartmentQuery request, CancellationToken cancellationToken)
    {
        var summary = await departments.GetSummaryAsync(request.Id, cancellationToken);
        return summary is null ? Error.NotFound("Department not found.") : DepartmentDto.From(summary);
    }
}
