using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.ResetUserPassword;

namespace Atlas.Application.Tests.Users;

public class ResetPasswordTests
{
    [Fact]
    public async Task Reset_sets_generated_temporary_password_and_forces_change()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john", mustChange: false, password: "Old#Passw0rd-xyz1");
        var stamp = user.SecurityStamp;

        var response = await app.Ok(new ResetUserPasswordCommand(user.Id));

        Assert.Equal(FakePasswordGenerator.Value, response.TemporaryPassword);
        Assert.True(response.MustChangePassword);
        Assert.True(user.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashOf(FakePasswordGenerator.Value), user.PasswordHash);
        Assert.NotEqual(stamp, user.SecurityStamp);
    }

    [Fact]
    public async Task Admin_cannot_reset_own_password_through_this_flow()
    {
        using var app = new AppFixture();
        var admin = app.SignInAsAdmin();

        await app.Fail(new ResetUserPasswordCommand(admin.Id), ErrorType.BusinessRule, "cannot_reset_own_password");
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await app.Fail(new ResetUserPasswordCommand(Guid.NewGuid()), ErrorType.NotFound);
    }
}
