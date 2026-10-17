using Atlas.API.Contracts;
using Atlas.API.Security;
using Atlas.Application.Common;
using Atlas.Application.Departments;
using Atlas.Application.Departments.ClearDepartmentHead;
using Atlas.Application.Departments.CreateDepartment;
using Atlas.Application.Departments.GetDepartment;
using Atlas.Application.Departments.ListDepartmentMembers;
using Atlas.Application.Departments.ListDepartments;
using Atlas.Application.Departments.SetDepartmentHead;
using Atlas.Application.Departments.UpdateDepartment;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Controllers;

/// <summary>Department administration (administrators only). A department is valid with no members and with no head.</summary>
[Route("api/v1/admin/departments")]
[Authorize(Policy = Policies.AdminOnly)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AdminDepartmentsController(ISender sender) : ApiControllerBase
{
    /// <summary>Create a department (no members, no head). Its shared data space exists immediately.</summary>
    [HttpPost]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDto>> Create(CreateDepartmentRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateDepartmentCommand(request.Name, request.Description), cancellationToken);
        if (result.IsFailure) return Fail(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>List departments (paged) with head and member count.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DepartmentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DepartmentDto>>> List([FromQuery] ListDepartmentsRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new ListDepartmentsQuery(request.Page, request.PageSize, request.Search), cancellationToken));

    /// <summary>Get a department.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetDepartmentQuery(id), cancellationToken));

    /// <summary>Rename / re-describe a department.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DepartmentDto>> Update(Guid id, UpdateDepartmentRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new UpdateDepartmentCommand(id, request.Name, request.Description), cancellationToken));

    /// <summary>List the members of a department (an empty department returns an empty page).</summary>
    [HttpGet("{id:guid}/members")]
    [ProducesResponseType<PagedResult<DepartmentMemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<DepartmentMemberDto>>> Members(Guid id, [FromQuery] ListDepartmentMembersRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new ListDepartmentMembersQuery(id, request.Page, request.PageSize), cancellationToken));

    /// <summary>Designate or replace the department head.</summary>
    /// <remarks>The user must already be a member of this department. The previous head stays a regular member.</remarks>
    [HttpPut("{id:guid}/head")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DepartmentDto>> SetHead(Guid id, SetDepartmentHeadRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new SetDepartmentHeadCommand(id, request.UserId), cancellationToken));

    /// <summary>Remove the department head without a replacement. The department keeps working. Idempotent.</summary>
    [HttpDelete("{id:guid}/head")]
    [ProducesResponseType<DepartmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DepartmentDto>> ClearHead(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new ClearDepartmentHeadCommand(id), cancellationToken));
}
