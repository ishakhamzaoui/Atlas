using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.ListUsers;

public sealed record ListUsersQuery(int Page, int PageSize, string? Search, UserRole? Role, bool? IsActive)
    : IRequest<Result<PagedResult<UserDto>>>, IRequireAdmin;

public sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public const int MaxPageSize = 100;

    public ListUsersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(x => x.Search).MaximumLength(UsernamePolicy.MaxLength);
        RuleFor(x => x.Role).IsInEnum().When(x => x.Role.HasValue);
    }
}

public sealed class ListUsersQueryHandler(IUserRepository users)
    : IRequestHandler<ListUsersQuery, Result<PagedResult<UserDto>>>
{
    public async Task<Result<PagedResult<UserDto>>> Handle(ListUsersQuery request, CancellationToken cancellationToken)
    {
        var page = await users.ListAsync(
            new UserListFilter(request.Page, request.PageSize, request.Search, request.Role, request.IsActive),
            cancellationToken);
        return page.Map(u => u.ToDto());
    }
}
