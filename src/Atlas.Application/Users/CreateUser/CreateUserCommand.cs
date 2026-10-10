using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Users;
using FluentValidation;
using MediatR;

namespace Atlas.Application.Users.CreateUser;

public sealed record CreateUserCommand(string Username, string Password, UserRole Role)
    : IRequest<UserDto>, IRequireAdmin;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Username).MustBeValidUsername();
        RuleFor(x => x.Password).MustMeetPasswordPolicy(x => x.Username);
        RuleFor(x => x.Role).IsInEnum();
    }
}

public sealed class CreateUserCommandHandler(IUserRepository users, IPasswordHasher hasher, TimeProvider clock)
    : IRequestHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var normalized = UsernamePolicy.Normalize(request.Username);
        if (await users.UsernameExistsAsync(normalized, null, cancellationToken))
            throw new ConflictException("username_taken", "That username is already in use.");

        // Admin-issued passwords are always temporary: the user must choose their own at first login.
        var user = User.Create(request.Username, hasher.Hash(request.Password), request.Role,
            mustChangePassword: true, clock.GetUtcNow());

        await users.AddAsync(user, cancellationToken); // unique index is the final arbiter under concurrency
        return user.ToDto();
    }
}
