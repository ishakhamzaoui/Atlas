using Atlas.Domain.Users;

namespace Atlas.Domain.Tests.Users;

public class UsernamePolicyTests
{
    [Theory]
    [InlineData("Admin", "ADMIN")]
    [InlineData("  jDoe  ", "JDOE")]
    [InlineData("a.b_c-d", "A.B_C-D")]
    public void Normalize_is_trimmed_and_case_insensitive(string input, string expected) =>
        Assert.Equal(expected, UsernamePolicy.Normalize(input));

    [Fact]
    public void Case_only_differences_normalize_to_the_same_value() =>
        Assert.Equal(UsernamePolicy.Normalize("JohnDoe"), UsernamePolicy.Normalize("johndoe"));

    [Theory]
    [InlineData("john")]
    [InlineData("j.doe-1_x")]
    [InlineData("abc")]
    public void Valid_usernames_pass(string username) => Assert.Empty(UsernamePolicy.Validate(username));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData("_leading")]
    [InlineData("-leading")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("john@example.com")]
    [InlineData("üser")]
    public void Invalid_usernames_fail(string? username) => Assert.NotEmpty(UsernamePolicy.Validate(username));

    [Fact]
    public void Too_long_username_fails() =>
        Assert.NotEmpty(UsernamePolicy.Validate(new string('a', UsernamePolicy.MaxLength + 1)));
}
