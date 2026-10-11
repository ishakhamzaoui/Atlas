using Atlas.API.Contracts;
using Atlas.API.Security;
using Atlas.Application.Auth;
using Atlas.Application.Auth.ChangePassword;
using Atlas.Application.Auth.GetCurrentUser;
using Atlas.Application.Auth.Login;
using Atlas.Application.Auth.Logout;
using Atlas.Application.Auth.Refresh;
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

    /// <summary>Exchange a refresh token for a new access token and a new refresh token.</summary>
    /// <remarks>
    /// Refresh tokens are single-use: the presented token is retired and a successor is returned. Presenting a token that
    /// was already used revokes the whole session (theft detection). Tokens also die on password change/reset, deactivation,
    /// role change/rename, logout and when the session reaches its absolute lifetime.
    /// </remarks>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<LoginResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken));

    /// <summary>End the session that the refresh token belongs to.</summary>
    /// <remarks>
    /// Always returns 204 (it does not reveal whether the token existed). Already-issued access tokens stay valid
    /// until they expire (at most a few minutes).
    /// </remarks>
    [HttpPost("logout")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
        return NoContent();
    }

    /// <summary>Change the caller's password (mandatory on first login).</summary>
    /// <remarks>
    /// Requires the current password. On success all earlier sessions (access and refresh tokens, every device) are invalidated
    /// and a fresh, unrestricted access token plus refresh token is returned.
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
