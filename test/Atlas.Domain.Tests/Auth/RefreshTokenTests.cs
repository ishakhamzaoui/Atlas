using Atlas.Domain.Auth;
using Atlas.Domain.Common;

namespace Atlas.Domain.Tests.Auth;

public class RefreshTokenTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RefreshToken Issue(TimeSpan? lifetime = null, DateTimeOffset? absolute = null) =>
        RefreshToken.Issue(Guid.NewGuid(), "hash", Guid.NewGuid(), Guid.NewGuid(), T0,
            lifetime ?? TimeSpan.FromDays(7), absolute ?? T0.AddDays(30));

    [Fact]
    public void Issue_sets_sliding_expiry_when_below_the_absolute_cap()
    {
        var token = Issue();
        Assert.Equal(T0.AddDays(7), token.ExpiresAt);
        Assert.False(token.IsRevoked);
        Assert.False(token.IsExpired(T0.AddDays(6)));
        Assert.True(token.IsExpired(T0.AddDays(7)));
    }

    [Fact]
    public void Expiry_is_capped_at_the_absolute_limit() =>
        Assert.Equal(T0.AddDays(3), Issue(absolute: T0.AddDays(3)).ExpiresAt);

    [Fact]
    public void Cannot_issue_past_the_absolute_limit() =>
        Assert.Throws<DomainException>(() => Issue(absolute: T0));

    [Fact]
    public void Blank_hash_is_rejected() =>
        Assert.Throws<DomainException>(() =>
            RefreshToken.Issue(Guid.NewGuid(), " ", Guid.NewGuid(), Guid.NewGuid(), T0, TimeSpan.FromDays(1), T0.AddDays(2)));

    [Fact]
    public void Revoke_is_idempotent_and_remembers_the_successor()
    {
        var token = Issue();
        var successor = Guid.NewGuid();

        token.Revoke(T0.AddHours(1), successor);
        token.Revoke(T0.AddHours(2), Guid.NewGuid());

        Assert.True(token.IsRevoked);
        Assert.Equal(T0.AddHours(1), token.RevokedAt);
        Assert.Equal(successor, token.ReplacedByTokenId);
    }
}
