using System.Text;
using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _clock;
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Create(User user)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        // Only what is needed to authorise a request. No password, hash or secret ever goes in here.
        var claims = new Dictionary<string, object>
        {
            [AuthClaims.Subject] = user.Id.ToString(),
            [AuthClaims.UniqueName] = user.Username,
            [AuthClaims.Role] = user.Role.ToString(),
            [AuthClaims.SecurityStamp] = user.SecurityStamp.ToString("N")
        };
        if (user.MustChangePassword)
            claims[AuthClaims.MustChangePassword] = "true";

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Claims = claims,
            SigningCredentials = _credentials
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
