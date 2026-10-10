using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Users;
using MediatR;

namespace Atlas.Application.Auth.GetCurrentUser;

public sealed record GetCurrentUserQuery : IRequest<UserDto>;

public sealed class GetCurrentUserQueryHandler(IUserRepository users, ICurrentUser currentUser)
    : IRequestHandler<GetCurrentUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetCurrentUserQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("unauthenticated", "Authentication is required.");
        var user = await users.GetByIdAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException("unauthenticated", "Authentication is required.");
        return user.ToDto();
    }
}
