using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Application.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Departments.ListDepartmentMembers;

/// <summary>Members of one department (an empty department returns an empty page), flagging the head when there is one.</summary>
public sealed record ListDepartmentMembersQuery(Guid DepartmentId, int Page, int PageSize)
    : IRequest<Result<PagedResult<DepartmentMemberDto>>>, IRequireAdmin;

public sealed class ListDepartmentMembersQueryValidator : AbstractValidator<ListDepartmentMembersQuery>
{
    public const int MaxPageSize = 100;

    public ListDepartmentMembersQueryValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public sealed class ListDepartmentMembersQueryHandler(IDepartmentRepository departments, IUserRepository users)
    : IRequestHandler<ListDepartmentMembersQuery, Result<PagedResult<DepartmentMemberDto>>>
{
    public async Task<Result<PagedResult<DepartmentMemberDto>>> Handle(ListDepartmentMembersQuery request, CancellationToken cancellationToken)
    {
        var department = await departments.GetByIdAsync(request.DepartmentId, cancellationToken);
        if (department is null)
            return Error.NotFound("Department not found.");

        var page = await users.ListAsync(
            new UserListFilter(request.Page, request.PageSize, null, null, null, DepartmentId: department.Id),
            cancellationToken);

        return page.Map(u => new DepartmentMemberDto(u.ToDto(), department.IsHead(u.Id)));
    }
}
