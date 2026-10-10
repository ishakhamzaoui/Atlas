using System.Text;
using Atlas.Application.Common;
using Atlas.Domain.Users;
using Atlas.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.Infrastructure.Tests.Security;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "atlas-test",
        Audience = "atlas-test-clients",
        SigningKey = "unit-test-signing-key-which-is-long-enough-0123456789",
        AccessTokenMinutes = 15
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JwtTokenService Service(DateTimeOffset? now = null) =>
        new(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock(now ?? DateTimeOffset.UtcNow));

    private static TokenValidationParameters Parameters(string? key = null) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Options.Issuer,
        ValidateAudience = true,
        ValidAudience = Options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? Options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = AuthClaims.Role,
        NameClaimType = AuthClaims.UniqueName
    };

    private static User NewUser(bool mustChange, UserRole role = UserRole.User) =>
        User.Create("john", "bcrypt-hash-placeholder", role, mustChange, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Token_validates_and_contains_only_the_expected_claims()
    {
        var user = NewUser(mustChange: false, UserRole.Admin);
        var token = Service().Create(user);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Parameters());

        Assert.True(result.IsValid);
        var claims = result.Claims;
        Assert.Equal(user.Id.ToString(), claims[AuthClaims.Subject]);
        Assert.Equal("john", claims[AuthClaims.UniqueName]);
        Assert.Equal("Admin", claims[AuthClaims.Role]);
        Assert.Equal(user.SecurityStamp.ToString("N"), claims[AuthClaims.SecurityStamp]);
        Assert.False(claims.ContainsKey(AuthClaims.MustChangePassword));

        // Nothing secret leaks into the token.
        Assert.DoesNotContain(claims.Keys, k => k.Contains("password", StringComparison.OrdinalIgnoreCase) && k != AuthClaims.MustChangePassword);
        Assert.DoesNotContain("bcrypt-hash-placeholder", claims.Values.Select(v => v?.ToString()));
    }

    [Fact]
    public async Task Token_for_account_that_must_change_password_is_marked()
    {
        var token = Service().Create(NewUser(mustChange: true));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Parameters());

        Assert.True(result.IsValid);
        Assert.Equal("true", result.Claims[AuthClaims.MustChangePassword]);
    }

    [Fact]
    public void Expiry_matches_configuration()
    {
        var now = new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        var token = Service(now).Create(NewUser(false));

        Assert.Equal(now.AddMinutes(15), token.ExpiresAt);
    }

    [Fact]
    public async Task Token_signed_with_another_key_is_rejected()
    {
        var token = Service().Create(NewUser(false));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Parameters("a-completely-different-signing-key-0123456789"));
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var token = Service(DateTimeOffset.UtcNow.AddHours(-2)).Create(NewUser(false));
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, Parameters());
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Wrong_audience_or_issuer_is_rejected()
    {
        var token = Service().Create(NewUser(false));

        var badAudience = Parameters();
        badAudience.ValidAudience = "someone-else";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, badAudience)).IsValid);

        var badIssuer = Parameters();
        badIssuer.ValidIssuer = "someone-else";
        Assert.False((await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, badIssuer)).IsValid);
    }
}

public class JwtOptionsValidatorTests
{
    private readonly JwtOptionsValidator _validator = new();

    private static JwtOptions Valid() => new()
    {
        Issuer = "i", Audience = "a", SigningKey = new string('k', 48), AccessTokenMinutes = 15
    };

    [Fact]
    public void Valid_options_pass() => Assert.True(_validator.Validate(null, Valid()).Succeeded);

    [Fact]
    public void Missing_signing_key_fails()
    {
        var o = Valid();
        o.SigningKey = "";
        Assert.True(_validator.Validate(null, o).Failed);
    }

    [Fact]
    public void Short_signing_key_fails()
    {
        var o = Valid();
        o.SigningKey = "too-short";
        Assert.True(_validator.Validate(null, o).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Unreasonable_lifetime_fails(int minutes)
    {
        var o = Valid();
        o.AccessTokenMinutes = minutes;
        Assert.True(_validator.Validate(null, o).Failed);
    }

    [Fact]
    public void Missing_issuer_or_audience_fails()
    {
        var o = Valid();
        o.Issuer = "";
        Assert.True(_validator.Validate(null, o).Failed);

        o = Valid();
        o.Audience = "";
        Assert.True(_validator.Validate(null, o).Failed);
    }
}
