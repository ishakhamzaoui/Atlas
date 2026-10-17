using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Results;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Departments.ClearDepartmentHead;

/// <summary>Removes the head without appointing a replacement; the department stays fully functional. Idempotent.</summary>
public sealed record ClearDepartmentHeadCommand(Guid DepartmentId) : IRequest<Result<DepartmentDto>>, IRequireAdmin;

public sealed class ClearDepartmentHeadCommandValidator : AbstractValidator<ClearDepartmentHeadCommand>
{
    public ClearDepartmentHeadCommandValidator() => RuleFor(x => x.DepartmentId).NotEmpty();
}

public sealed class ClearDepartmentHeadCommandHandler(
    IDepartmentRepository departments,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<ClearDepartmentHeadCommandHandler> logger) : IRequestHandler<ClearDepartmentHeadCommand, Result<DepartmentDto>>
{
    public Task<Result<DepartmentDto>> Handle(ClearDepartmentHeadCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<DepartmentDto>>(async ct =>
        {
            var department = await departments.GetByIdAsync(request.DepartmentId, ct);
            if (department is null) return Error.NotFound("Department not found.");

            if (department.HeadUserId is { } previousHead)
            {
                department.ClearHead(clock.GetUtcNow());
                await departments.SaveChangesAsync(ct);
                logger.LogInformation("Admin {ActorId} removed head {PreviousHeadId} of department {DepartmentId}; no replacement required",
                    currentUser.UserId, previousHead, department.Id);
            }

            return DepartmentDto.From((await departments.GetSummaryAsync(department.Id, ct))!);
        }, cancellationToken);
}
