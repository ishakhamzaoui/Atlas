using Atlas.Application.Common;
using Atlas.Application.Users;
using Atlas.Domain.Users;

namespace Atlas.Application.Auth;

/// <param name="AccessToken">Signed JWT bearer token.</param>
/// <param name="TokenType">Always "Bearer".</param>
/// <param name="ExpiresIn">Seconds until the token expires.</param>
/// <param name="MustChangePassword">
/// When true the token is restricted to <c>change-password</c> and <c>me</c> until the password is changed.
/// </param>
public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    DateTimeOffset ExpiresAt,
    bool MustChangePassword,
    UserDto User)
{
    public static LoginResponse From(User user, AccessToken token, DateTimeOffset now) =>
        new(token.Value, "Bearer", Math.Max(0, (int)(token.ExpiresAt - now).TotalSeconds), token.ExpiresAt,
            user.MustChangePassword, user.ToDto());
}
