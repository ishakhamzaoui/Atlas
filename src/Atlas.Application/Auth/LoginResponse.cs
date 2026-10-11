using Atlas.Application.Users;

namespace Atlas.Application.Auth;

/// <param name="AccessToken">Signed, short-lived JWT bearer token.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresIn">Seconds until the access token expires.</param>
/// <param name="MustChangePassword">
/// When true the access token is restricted to <c>change-password</c> and <c>me</c>, and no refresh token is issued.
/// </param>
/// <param name="RefreshToken">
/// Opaque, single-use token for <c>POST /api/v1/auth/refresh</c>. Store it securely; it is shown only once.
/// Null while a password change is pending.
/// </param>
public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset ExpiresAt,
    bool MustChangePassword,
    UserDto User,
    string? RefreshToken = null,
    DateTimeOffset? RefreshTokenExpiresAt = null);
