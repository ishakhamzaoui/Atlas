using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Departments.UpdateDepartment;

/// <summary>Renames / re-describes a department. Membership and leadership have their own use cases.</summary>
public sealed record UpdateDepartmentCommand(Guid Id, string Name, string? Description) : IRequest<Result<DepartmentDto>>, IRequireAdmin;

public sealed class UpdateDepartmentCommandValidator : AbstractValidator<UpdateDepartmentCommand>
{
    public UpdateDepartmentCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).MustBeValidOrganizationName();
        RuleFor(x => x.Description).MustBeValidDescription();
    }
}

public sealed class UpdateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<UpdateDepartmentCommandHandler> logger) : IRequestHandler<UpdateDepartmentCommand, Result<DepartmentDto>>
{
    public Task<Result<DepartmentDto>> Handle(UpdateDepartmentCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<DepartmentDto>>(async ct =>
        {
            var department = await departments.GetByIdAsync(request.Id, ct);
            if (department is null)
                return Error.NotFound("Department not found.");

            var normalized = OrganizationNamePolicy.Normalize(request.Name);
            if (normalized != department.NormalizedName && await departments.NameExistsAsync(normalized, department.Id, ct))
                return Error.Conflict("department_name_taken", "A department with that name already exists.");

            department.Update(request.Name, request.Description, clock.GetUtcNow());
            try
            {
                await departments.SaveChangesAsync(ct);
            }
            catch (PersistenceConflictException ex)
            {
                return PersistenceErrors.From(ex);
            }

            logger.LogInformation("Admin {ActorId} updated department {DepartmentId}", currentUser.UserId, department.Id);
            return DepartmentDto.From((await departments.GetSummaryAsync(department.Id, ct))!);
        }, cancellationToken);
}
