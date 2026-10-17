using Atlas.API.Contracts;
using Atlas.API.Security;
using Atlas.Application.Common;
using Atlas.Application.Profiles;
using Atlas.Application.Profiles.CreateProfile;
using Atlas.Application.Profiles.GetProfile;
using Atlas.Application.Profiles.ListProfiles;
using Atlas.Application.Profiles.UpdateProfile;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Controllers;

/// <summary>Profile administration (administrators only). Profiles are never deleted: they stay available for reassignment.</summary>
[Route("api/v1/admin/profiles")]
[Authorize(Policy = Policies.AdminOnly)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
public sealed class AdminProfilesController(ISender sender) : ApiControllerBase
{
    /// <summary>Create a profile (it is not assigned to anyone yet).</summary>
    [HttpPost]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProfileDto>> Create(CreateProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateProfileCommand(request.Name, request.Description), cancellationToken);
        if (result.IsFailure) return Fail(result.Error!);

        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>List profiles (paged), optionally only assigned or only unassigned ones.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ProfileDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProfileDto>>> List([FromQuery] ListProfilesRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new ListProfilesQuery(request.Page, request.PageSize, request.Search, request.Assigned), cancellationToken));

    /// <summary>Get a profile with the number of users currently holding it.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfileDto>> Get(Guid id, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetProfileQuery(id), cancellationToken));

    /// <summary>Rename / re-describe a profile.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<ProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProfileDto>> Update(Guid id, UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new UpdateProfileCommand(id, request.Name, request.Description), cancellationToken));
}
