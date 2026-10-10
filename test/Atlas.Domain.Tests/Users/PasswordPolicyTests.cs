using Atlas.Domain.Users;

namespace Atlas.Domain.Tests.Users;

public class PasswordPolicyTests
{
    [Fact]
    public void Strong_password_passes() =>
        Assert.Empty(PasswordPolicy.Validate("Str0ng#Passw0rd!", "jdoe"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Sh0rt#pw")]                 // too short
    [InlineData("alllowercase#12345")]       // no upper
    [InlineData("ALLUPPERCASE#12345")]       // no lower
    [InlineData("NoDigitsHere#Abcdef")]      // no digit
    [InlineData("NoSymbolsHere12345Ab")]     // no symbol
    public void Weak_passwords_fail(string? password) =>
        Assert.NotEmpty(PasswordPolicy.Validate(password));

    [Fact]
    public void Password_containing_username_fails() =>
        Assert.NotEmpty(PasswordPolicy.Validate("My-JDoe-Passw0rd!", "jdoe"));

    [Fact]
    public void Overlong_password_fails() =>
        Assert.NotEmpty(PasswordPolicy.Validate("Aa1#" + new string('x', PasswordPolicy.MaxLength)));
}
