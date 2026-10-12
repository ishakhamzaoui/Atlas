using Atlas.Application.Auth.ChangePassword;
using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;

namespace Atlas.Application.Tests.Auth;

public class ChangePasswordTests
{
    private const string Temp = "Temporary#Pass1x";
    private const string NewPassword = "Brand#New-Passw0rd";

    [Fact]
    public async Task Successful_change_clears_flag_rehashes_rotates_stamp_and_returns_unrestricted_token()
    {
        using var app = new AppFixture();
        var user = app.Users.Seed("john", mustChange: true, password: Temp);
        app.Current.SignInAs(user);
        var stamp = user.SecurityStamp;

        var result = await app.Ok(new ChangePasswordCommand(Temp, NewPassword, NewPassword));

        Assert.False(user.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashOf(NewPassword), user.PasswordHash);
        Assert.NotEqual(stamp, user.SecurityStamp);
        Assert.False(result.MustChangePassword);
        Assert.EndsWith("-full", result.AccessToken);
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected_and_flag_stays()
    {
        using var app = new AppFixture();
        var user = app.Users.Seed("john", mustChange: true, password: Temp);
        app.Current.SignInAs(user);

        await app.Fail(new ChangePasswordCommand("Wrong#Passw0rd1", NewPassword, NewPassword),
            ErrorType.BusinessRule, "invalid_current_password");

        Assert.True(user.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashOf(Temp), user.PasswordHash);
    }

    [Fact]
    public async Task New_password_must_differ_from_current()
    {
        using var app = new AppFixture();
        var user = app.Users.Seed("john", mustChange: true, password: Temp);
        app.Current.SignInAs(user);

        await app.Fail(new ChangePasswordCommand(Temp, Temp, Temp), ErrorType.BusinessRule, "password_unchanged");

        Assert.True(user.MustChangePassword);
    }

    [Theory]
    [InlineData("weak", "weak")]
    [InlineData("Brand#New-Passw0rd", "Different#Passw0rd9")] // confirmation mismatch
    public async Task Weak_or_unconfirmed_new_password_fails_validation(string newPassword, string confirm)
    {
        using var app = new AppFixture();
        var user = app.Users.Seed("john", mustChange: true, password: Temp);
        app.Current.SignInAs(user);

        await app.Fail(new ChangePasswordCommand(Temp, newPassword, confirm), ErrorType.Validation);
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task Anonymous_caller_is_unauthorized()
    {
        using var app = new AppFixture();
        await app.Fail(new ChangePasswordCommand(Temp, NewPassword, NewPassword), ErrorType.Unauthorized, "unauthenticated");
    }
}
