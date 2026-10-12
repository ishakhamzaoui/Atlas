using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.GetUser;

public sealed record GetUserQuery(Guid Id) : IRequest<Result<UserDto>>, IRequireAdmin;

public sealed class GetUserQueryValidator : AbstractValidator<GetUserQuery>
{
    public GetUserQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetUserQueryHandler(IUserRepository users) : IRequestHandler<GetUserQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.Id, cancellationToken);
        return user is null ? Error.NotFound("User not found.") : user.ToDto();
    }
}
