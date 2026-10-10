using System.Text;
using Atlas.Application.Common;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.API.Tests.Support;

/// <summary>Forges tokens (valid or deliberately broken) to exercise the bearer validation pipeline.</summary>
public static class TestTokens
{
    public static string Create(
        string signingKey,
        Guid userId,
        Guid securityStamp,
        string role = "User",
        string issuer = AtlasApiFactory.Issuer,
        string audience = AtlasApiFactory.Audience,
        DateTime? notBefore = null,
        DateTime? expires = null,
        bool mustChangePassword = false)
    {
        var claims = new Dictionary<string, object>
        {
            [AuthClaims.Subject] = userId.ToString(),
            [AuthClaims.UniqueName] = "forged",
            [AuthClaims.Role] = role,
            [AuthClaims.SecurityStamp] = securityStamp.ToString("N")
        };
        if (mustChangePassword) claims[AuthClaims.MustChangePassword] = "true";

        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            NotBefore = notBefore ?? now.AddMinutes(-1),
            IssuedAt = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddMinutes(10),
            Claims = claims,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256)
        });
    }
}
