using Atlas.Application.Spaces;
using Atlas.Application.Spaces.GetMySpaces;
using Atlas.Application.Spaces.GetSpace;
using Atlas.Domain.Spaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.API.Controllers;

/// <summary>
/// The three data spaces (private, department, public) and the caller's authorization to them. A space EXISTING is not the same as
/// the caller being AUTHORIZED to use it. This phase defines only who may enter/read each space; item-level read/write rules come later.
/// Any authenticated account may call these endpoints (orphans included); access is decided per space.
/// </summary>
[Route("api/v1/spaces")]
[Authorize] // default policy: a valid, fully-activated session
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class SpacesController(ISender sender) : ApiControllerBase
{
    /// <summary>My spaces and my effective access to each, from the current organizational state.</summary>
    [HttpGet("me")]
    [ProducesResponseType<MySpacesDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MySpacesDto>> Mine(CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetMySpacesQuery(), cancellationToken));

    /// <summary>The public space: readable by every authenticated user, with or without a department or profile.</summary>
    [HttpGet("public")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SpaceDto>> Public(CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetSpaceQuery(DataSpaceKind.Public, null), cancellationToken));

    /// <summary>A user's private space. Only the owner, and only while they hold organizational privileges (department and profile).</summary>
    [HttpGet("private/{userId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SpaceDto>> Private(Guid userId, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetSpaceQuery(DataSpaceKind.Private, userId), cancellationToken));

    /// <summary>A department's shared space. Only members of that department who hold a profile.</summary>
    [HttpGet("departments/{departmentId:guid}")]
    [ProducesResponseType<SpaceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SpaceDto>> Department(Guid departmentId, CancellationToken cancellationToken) =>
        Respond(await sender.Send(new GetSpaceQuery(DataSpaceKind.Department, departmentId), cancellationToken));
}
