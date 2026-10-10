using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.GetUser;

public sealed record GetUserQuery(Guid Id) : IRequest<UserDto>, IRequireAdmin;

public sealed class GetUserQueryValidator : AbstractValidator<GetUserQuery>
{
    public GetUserQueryValidator() => RuleFor(x => x.Id).NotEmpty();
}

public sealed class GetUserQueryHandler(IUserRepository users) : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(request.Id, cancellationToken)
                   ?? throw new NotFoundException("User not found.");
        return user.ToDto();
    }
}
