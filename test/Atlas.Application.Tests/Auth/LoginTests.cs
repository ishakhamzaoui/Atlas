using Atlas.Application.Auth.Login;
using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;

namespace Atlas.Application.Tests.Auth;

public class LoginTests
{
    private const string Password = "Current#Passw0rd1";

    [Fact]
    public async Task Valid_credentials_return_bearer_token_and_safe_user()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);

        var result = await app.Ok(new LoginCommand("JOHN", Password)); // case-insensitive username

        Assert.Equal("Bearer", result.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.InRange(result.ExpiresIn, 1, 900);
        Assert.False(result.MustChangePassword);
        Assert.Equal("john", result.User.Username);
    }

    [Fact]
    public async Task First_login_reports_must_change_password()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", mustChange: true, password: Password);

        var result = await app.Ok(new LoginCommand("john", Password));

        Assert.True(result.MustChangePassword);
        Assert.True(result.User.MustChangePassword);
    }

    [Fact]
    public async Task Unknown_user_wrong_password_and_inactive_account_are_indistinguishable()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);
        app.Users.Seed("sleeper", active: false, password: Password);

        var unknown = await app.Fail(new LoginCommand("ghost", Password), ErrorType.Unauthorized, "invalid_credentials");
        var wrong = await app.Fail(new LoginCommand("john", "Wrong#Passw0rd1"), ErrorType.Unauthorized, "invalid_credentials");
        var inactive = await app.Fail(new LoginCommand("sleeper", Password), ErrorType.Unauthorized, "invalid_credentials");

        Assert.Equal(unknown, wrong);
        Assert.Equal(unknown, inactive);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("john", "")]
    public async Task Missing_fields_fail_validation(string username, string password)
    {
        using var app = new AppFixture();
        await app.Fail(new LoginCommand(username, password), ErrorType.Validation);
    }
}
