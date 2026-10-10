using Atlas.Infrastructure.Security;

namespace Atlas.Infrastructure.Tests.Security;

public class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new(workFactor: 4); // low cost: tests only

    [Fact]
    public void Hash_is_a_bcrypt_hash_and_never_the_plaintext()
    {
        var hash = _hasher.Hash("Str0ng#Passw0rd!");

        Assert.StartsWith("$2", hash);
        Assert.DoesNotContain("Str0ng#Passw0rd!", hash);
        Assert.True(hash.Length <= 100); // fits the password_hash column
    }

    [Fact]
    public void Same_password_hashes_differently_each_time_but_both_verify()
    {
        var a = _hasher.Hash("Str0ng#Passw0rd!");
        var b = _hasher.Hash("Str0ng#Passw0rd!");

        Assert.NotEqual(a, b);
        Assert.True(_hasher.Verify("Str0ng#Passw0rd!", a));
        Assert.True(_hasher.Verify("Str0ng#Passw0rd!", b));
    }

    [Theory]
    [InlineData("str0ng#passw0rd!")]
    [InlineData("Str0ng#Passw0rd")]
    [InlineData("")]
    public void Wrong_password_is_rejected(string attempt) =>
        Assert.False(_hasher.Verify(attempt, _hasher.Hash("Str0ng#Passw0rd!")));

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("")]
    [InlineData("$2a$04$short")]
    public void Malformed_hash_returns_false_instead_of_throwing(string hash) =>
        Assert.False(_hasher.Verify("Str0ng#Passw0rd!", hash));

    [Fact]
    public void Passwords_longer_than_72_bytes_are_not_silently_truncated()
    {
        var prefix = new string('a', 80);
        var hash = _hasher.Hash(prefix + "X1#");

        Assert.False(_hasher.Verify(prefix + "Y2#", hash));
        Assert.True(_hasher.Verify(prefix + "X1#", hash));
    }

    [Fact]
    public void Dummy_hash_is_valid_but_matches_nothing_sensible()
    {
        Assert.StartsWith("$2", _hasher.DummyHash);
        Assert.False(_hasher.Verify("Str0ng#Passw0rd!", _hasher.DummyHash));
    }
}
