using System.Text.RegularExpressions;
using Atlas.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure.Tests.Security;

public class RefreshTokenProviderTests
{
    private static RefreshTokenProvider Provider(int days = 7, int absoluteDays = 30) =>
        new(Options.Create(new JwtOptions { RefreshTokenDays = days, RefreshTokenAbsoluteDays = absoluteDays }));

    [Fact]
    public void Generated_tokens_are_url_safe_high_entropy_and_unique()
    {
        var provider = Provider();
        var seen = new HashSet<string>();

        for (var i = 0; i < 500; i++)
        {
            var generated = provider.Generate();
            Assert.Matches(new Regex("^[A-Za-z0-9_-]{43}$"), generated.Token); // 256 bits, base64url, no padding
            Assert.True(seen.Add(generated.Token));
        }
    }

    [Fact]
    public void Hash_is_deterministic_lowercase_sha256_hex_and_not_the_token()
    {
        var provider = Provider();
        var generated = provider.Generate();

        Assert.Equal(generated.Hash, provider.Hash(generated.Token));
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), generated.Hash);
        Assert.DoesNotContain(generated.Token, generated.Hash);
        Assert.NotEqual(provider.Hash("a"), provider.Hash("b"));
    }

    [Fact]
    public void Lifetimes_come_from_configuration()
    {
        var provider = Provider(days: 3, absoluteDays: 12);
        Assert.Equal(TimeSpan.FromDays(3), provider.Lifetime);
        Assert.Equal(TimeSpan.FromDays(12), provider.AbsoluteLifetime);
    }
}

public class JwtOptionsRefreshValidationTests
{
    private readonly JwtOptionsValidator _validator = new();

    private static JwtOptions Valid() => new()
    {
        Issuer = "i", Audience = "a", SigningKey = new string('k', 48), AccessTokenMinutes = 15,
        RefreshTokenDays = 7, RefreshTokenAbsoluteDays = 30
    };

    [Theory]
    [InlineData(0, 30)]
    [InlineData(91, 100)]
    [InlineData(7, 6)]     // absolute shorter than sliding
    [InlineData(7, 400)]
    public void Unreasonable_refresh_lifetimes_fail(int days, int absoluteDays)
    {
        var o = Valid();
        o.RefreshTokenDays = days;
        o.RefreshTokenAbsoluteDays = absoluteDays;
        Assert.True(_validator.Validate(null, o).Failed);
    }

    [Fact]
    public void Default_refresh_lifetimes_are_valid() => Assert.True(_validator.Validate(null, Valid()).Succeeded);
}
