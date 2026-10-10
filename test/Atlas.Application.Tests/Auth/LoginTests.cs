using Atlas.Application.Auth.Login;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Tests.Support;
using FluentValidation;

namespace Atlas.Application.Tests.Auth;

public class LoginTests
{
    private const string Password = "Current#Passw0rd1";

    [Fact]
    public async Task Valid_credentials_return_bearer_token_and_safe_user()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);

        var result = await app.Send(new LoginCommand("JOHN", Password)); // case-insensitive username

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

        var result = await app.Send(new LoginCommand("john", Password));

        Assert.True(result.MustChangePassword);
        Assert.True(result.User.MustChangePassword);
    }

    [Fact]
    public async Task Unknown_user_wrong_password_and_inactive_account_are_indistinguishable()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);
        app.Users.Seed("sleeper", active: false, password: Password);

        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new LoginCommand("ghost", Password)));
        var wrong = await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new LoginCommand("john", "Wrong#Passw0rd1")));
        var inactive = await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new LoginCommand("sleeper", Password)));

        Assert.Equal(unknown.Message, wrong.Message);
        Assert.Equal(unknown.Message, inactive.Message);
        Assert.Equal(unknown.Code, wrong.Code);
        Assert.Equal(unknown.Code, inactive.Code);
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("john", "")]
    public async Task Missing_fields_fail_validation(string username, string password)
    {
        using var app = new AppFixture();
        await Assert.ThrowsAsync<ValidationException>(() => app.Send(new LoginCommand(username, password)));
    }
}
