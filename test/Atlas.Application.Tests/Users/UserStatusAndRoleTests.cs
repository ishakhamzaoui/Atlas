using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.SetUserStatus;
using Atlas.Application.Users.UpdateUser;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Users;

public class UserStatusAndRoleTests
{
    [Fact]
    public async Task Admin_can_deactivate_and_reactivate_a_user()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");
        var stamp = user.SecurityStamp;

        var off = await app.Ok(new SetUserStatusCommand(user.Id, false));
        Assert.False(off.IsActive);
        Assert.NotEqual(stamp, user.SecurityStamp); // existing tokens die

        var on = await app.Ok(new SetUserStatusCommand(user.Id, true));
        Assert.True(on.IsActive);
    }

    [Fact]
    public async Task Admin_can_deactivate_another_admin_while_an_active_admin_remains()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin("actor.admin");
        var other = app.Users.Seed("other.admin", UserRole.Admin);

        var dto = await app.Ok(new SetUserStatusCommand(other.Id, false));

        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task Sole_administrator_cannot_deactivate_themselves()
    {
        using var app = new AppFixture();
        var admin = app.SignInAsAdmin();

        await app.Fail(new SetUserStatusCommand(admin.Id, false), ErrorType.BusinessRule, "cannot_deactivate_self");
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task Deactivating_a_user_who_is_the_last_active_admin_is_blocked()
    {
        using var app = new AppFixture();
        var lastAdmin = app.Users.Seed("last.admin", UserRole.Admin);
        // acting admin is inactive in the store but still holds a (stale) admin identity: guard must still hold
        var staleActor = app.Users.Seed("stale.admin", UserRole.Admin, active: false);
        app.Current.SignInAs(staleActor);

        await app.Fail(new SetUserStatusCommand(lastAdmin.Id, false), ErrorType.BusinessRule, "last_active_admin");
    }

    [Fact]
    public async Task Admin_cannot_change_own_role()
    {
        using var app = new AppFixture();
        var admin = app.SignInAsAdmin();
        app.Users.Seed("another.admin", UserRole.Admin);

        await app.Fail(new UpdateUserCommand(admin.Id, admin.Username, UserRole.User), ErrorType.BusinessRule, "cannot_change_own_role");
        Assert.Equal(UserRole.Admin, admin.Role);
    }

    [Fact]
    public async Task Last_active_admin_cannot_be_demoted()
    {
        using var app = new AppFixture();
        var lastAdmin = app.Users.Seed("last.admin", UserRole.Admin);
        app.Current.SignInAs(app.Users.Seed("stale.admin", UserRole.Admin, active: false));

        await app.Fail(new UpdateUserCommand(lastAdmin.Id, lastAdmin.Username, UserRole.User), ErrorType.BusinessRule, "last_active_admin");
        Assert.Equal(UserRole.Admin, lastAdmin.Role);
    }

    [Fact]
    public async Task Admin_can_promote_and_demote_when_another_active_admin_exists()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");

        var promoted = await app.Ok(new UpdateUserCommand(user.Id, "john", UserRole.Admin));
        Assert.Equal("Admin", promoted.Role);

        var demoted = await app.Ok(new UpdateUserCommand(user.Id, "john", UserRole.User));
        Assert.Equal("User", demoted.Role);
    }

    [Fact]
    public async Task Rename_to_existing_username_conflicts_even_with_different_case()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        app.Users.Seed("taken.name");
        var user = app.Users.Seed("john");

        await app.Fail(new UpdateUserCommand(user.Id, "TAKEN.NAME", UserRole.User), ErrorType.Conflict, "username_taken");
        Assert.Equal("john", user.Username);
    }

    [Fact]
    public async Task Case_only_rename_of_own_username_is_allowed()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");

        var dto = await app.Ok(new UpdateUserCommand(user.Id, "John", UserRole.User));
        Assert.Equal("John", dto.Username);
    }
}
