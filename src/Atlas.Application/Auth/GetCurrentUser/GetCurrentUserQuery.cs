using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Results;
using Atlas.Application.Users;
using MediatR;

namespace Atlas.Application.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<Result<UserDto>>;

public sealed class GetCurrentUserQueryHandler(IUserRepository users, ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
            return Error.Unauthenticated();

        var user = await users.GetByIdAsync(userId, cancellationToken);
        return user is null ? Error.Unauthenticated() : user.ToDto();
    }
}
