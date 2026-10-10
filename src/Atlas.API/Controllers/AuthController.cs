using Atlas.API.Contracts;
using Atlas.API.Security;
using Atlas.Application.Auth;
using Atlas.Application.Auth.ChangePassword;
using Atlas.Application.Auth.GetCurrentUser;
using Atlas.Application.Auth.Login;
using Atlas.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Atlas.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>Authenticate with username and password and receive a JWT access token.</summary>
    /// <remarks>
    /// Failures are deliberately generic (401) and do not reveal whether the username exists or the account is inactive.
    /// If <c>mustChangePassword</c> is true, the token only works for <c>change-password</c> and <c>me</c>.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new LoginCommand(request.Username, request.Password), cancellationToken));

    /// <summary>Change the caller's password (mandatory on first login).</summary>
    /// <remarks>
    /// Requires the current password. On success earlier tokens are invalidated and a fresh, unrestricted token is returned.
    /// </remarks>
    [HttpPost("change-password")]
    [Authorize(Policy = Policies.Authenticated)]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, request.ConfirmNewPassword),
            cancellationToken));

    /// <summary>Basic profile of the authenticated user.</summary>
    [HttpGet("me")]
    [Authorize(Policy = Policies.Authenticated)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetCurrentUserQuery(), cancellationToken));
}
