using Atlas.API.Contracts;
using Atlas.API.Security;
using Atlas.Application.Common;
using Atlas.Application.Organization;
using Atlas.Application.Organization.AssignUserToDepartment;
using Atlas.Application.Organization.RemoveUserFromDepartment;
using Atlas.Application.Profiles.AssignProfile;
using Atlas.Application.Profiles.RemoveProfile;
using Atlas.Application.Users;
using Atlas.Application.Users.CreateUser;
using Atlas.Application.Users.GetUser;
using Atlas.Application.Users.ListUsers;
using Atlas.Application.Users.ResetUserPassword;
using Atlas.Application.Users.SetUserStatus;
using Atlas.Application.Users.UpdateUser;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Controllers;

/// <summary>User administration. Administrators only; enforced by policy here and again in the application layer.</summary>
[Route("api/v1/admin/users")]
[Authorize(Policy = Policies.AdminOnly)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AdminUsersController(ISender sender) : ApiControllerBase
{
    /// <summary>Create a user with a temporary password (must be changed at first login).</summary>
    [HttpPost]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateUserCommand(request.Username, request.Password, request.Role), cancellationToken);
        if (result.IsFailure) return Fail(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>List users (paged, filterable by username substring, role and status).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<UserDto>>> List([FromQuery] ListUsersRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(
            new ListUsersQuery(request.Page, request.PageSize, request.Search, request.Role, request.IsActive,
                request.DepartmentId, request.ProfileId, request.Orphan),
            cancellationToken));

    /// <summary>Get a user's safe profile.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetUserQuery(id), cancellationToken));

    /// <summary>Update username and role. Admins cannot change their own role; the last active admin cannot be demoted.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserDto>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new UpdateUserCommand(id, request.Username, request.Role), cancellationToken));

    /// <summary>Activate or deactivate an account. The last active admin (and yourself) cannot be deactivated.</summary>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserDto>> SetStatus(Guid id, SetUserStatusRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new SetUserStatusCommand(id, request.IsActive), cancellationToken));

    /// <summary>Issue a random temporary password (returned once) that must be changed at next login.</summary>
    /// <remarks>The existing password is never revealed. The user's current sessions are invalidated.</remarks>
    [HttpPost("{id:guid}/reset-password")]
    [ProducesResponseType<ResetPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ResetPasswordResponse>> ResetPassword(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new ResetUserPasswordCommand(id), cancellationToken));

    /// <summary>Put the user in a department: join (from no department) or move (from another department).</summary>
    /// <remarks>
    /// No transfer request or approval is needed. Membership is exclusive, so any previous membership ends. When the user MOVES
    /// from another department, the old department's head role (if held) ends and the profile association is removed (the profile
    /// is retained) so no access to the former department survives; pass <c>profileId</c> to give a profile in the same call.
    /// A user joining from no department keeps an existing profile unless <c>profileId</c> replaces it. The response lists these
    /// side effects explicitly. Administrator accounts cannot join departments.
    /// </remarks>
    [HttpPut("{id:guid}/department")]
    [ProducesResponseType<OrganizationChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrganizationChangeDto>> AssignDepartment(Guid id, AssignDepartmentRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new AssignUserToDepartmentCommand(id, request.DepartmentId, request.ProfileId), cancellationToken));

    /// <summary>Take the user out of their department (the account is kept).</summary>
    /// <remarks>
    /// The department head role (if held) ends without a replacement, and the profile association is removed (the profile is
    /// retained), leaving an orphan user who can only read the public space. Idempotent.
    /// </remarks>
    [HttpDelete("{id:guid}/department")]
    [ProducesResponseType<OrganizationChangeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationChangeDto>> RemoveDepartment(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new RemoveUserFromDepartmentCommand(id), cancellationToken));

    /// <summary>Assign or replace the user's profile (independent of department membership).</summary>
    /// <remarks>The replaced profile is retained and stays available for other users. Administrator accounts cannot hold profiles.</remarks>
    [HttpPut("{id:guid}/profile")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<UserDto>> AssignProfile(Guid id, AssignProfileRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new AssignProfileCommand(id, request.ProfileId), cancellationToken));

    /// <summary>Remove the user's profile association. The profile itself is retained. Idempotent.</summary>
    [HttpDelete("{id:guid}/profile")]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> RemoveProfile(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new RemoveProfileCommand(id), cancellationToken));
}
