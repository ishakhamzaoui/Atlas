using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Departments.SetDepartmentHead;

/// <summary>
/// Designates or replaces the department head. The user must already be a member of this department, so a user who is not
/// in a department can never be a head. The previous head (if any) simply stays a regular member.
/// </summary>
public sealed record SetDepartmentHeadCommand(Guid DepartmentId, Guid UserId) : IRequest<Result<DepartmentDto>>, IRequireAdmin;

public sealed class SetDepartmentHeadCommandValidator : AbstractValidator<SetDepartmentHeadCommand>
{
    public SetDepartmentHeadCommandValidator()
    {
        RuleFor(x => x.DepartmentId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
    }
}

public sealed class SetDepartmentHeadCommandHandler(
    IDepartmentRepository departments,
    IUserRepository users,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<SetDepartmentHeadCommandHandler> logger) : IRequestHandler<SetDepartmentHeadCommand, Result<DepartmentDto>>
{
    public Task<Result<DepartmentDto>> Handle(SetDepartmentHeadCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<DepartmentDto>>(async ct =>
        {
            var department = await departments.GetByIdAsync(request.DepartmentId, ct);
            if (department is null) return Error.NotFound("Department not found.");

            var user = await users.GetByIdAsync(request.UserId, ct);
            if (user is null) return Error.NotFound("User not found.");

            if (user.DepartmentId != department.Id)
                return Error.BusinessRule("head_must_be_department_member",
                    "A department head must be a member of the department they lead. Assign the user to the department first.");

            var previousHead = department.HeadUserId;
            department.AppointHead(user.Id, clock.GetUtcNow());
            await departments.SaveChangesAsync(ct);

            logger.LogInformation("Admin {ActorId} set user {UserId} as head of department {DepartmentId} (previous head {PreviousHeadId})",
                currentUser.UserId, user.Id, department.Id, previousHead);
            return DepartmentDto.From((await departments.GetSummaryAsync(department.Id, ct))!);
        }, cancellationToken);
}
