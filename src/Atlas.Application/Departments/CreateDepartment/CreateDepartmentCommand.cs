using Atlas.Application.Common.Abstractions;
using Atlas.Application.Common.Behaviors;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Common.Results;
using Atlas.Application.Common.Validation;
using Atlas.Domain.Organization;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Departments.CreateDepartment;

/// <summary>Creates a department with no members and no head. Its shared data space exists from this moment.</summary>
public sealed record CreateDepartmentCommand(string Name, string? Description) : IRequest<Result<DepartmentDto>>, IRequireAdmin;

public sealed class CreateDepartmentCommandValidator : AbstractValidator<CreateDepartmentCommand>
{
    public CreateDepartmentCommandValidator()
    {
        RuleFor(x => x.Name).MustBeValidOrganizationName();
        RuleFor(x => x.Description).MustBeValidDescription();
    }
}

public sealed class CreateDepartmentCommandHandler(
    IDepartmentRepository departments,
    IUserWriteScope scope,
    ICurrentUser currentUser,
    TimeProvider clock,
    ILogger<CreateDepartmentCommandHandler> logger) : IRequestHandler<CreateDepartmentCommand, Result<DepartmentDto>>
{
    public Task<Result<DepartmentDto>> Handle(CreateDepartmentCommand request, CancellationToken cancellationToken) =>
        scope.RunExclusiveAsync<Result<DepartmentDto>>(async ct =>
        {
            if (await departments.NameExistsAsync(OrganizationNamePolicy.Normalize(request.Name), null, ct))
                return Error.Conflict("department_name_taken", "A department with that name already exists.");

            var department = Department.Create(request.Name, request.Description, clock.GetUtcNow());
            try
            {
                await departments.AddAsync(department, ct);
            }
            catch (PersistenceConflictException ex)
            {
                return PersistenceErrors.From(ex);
            }

            logger.LogInformation("Admin {ActorId} created department {DepartmentId}", currentUser.UserId, department.Id);
            return DepartmentDto.From(new DepartmentSummary(department, 0, null));
        }, cancellationToken);
}
