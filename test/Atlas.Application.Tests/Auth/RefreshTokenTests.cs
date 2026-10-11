using Atlas.Application.Auth.ChangePassword;
using Atlas.Application.Auth.Login;
using Atlas.Application.Auth.Logout;
using Atlas.Application.Auth.Refresh;
using Atlas.Application.Common.Exceptions;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.ResetUserPassword;
using Atlas.Application.Users.UpdateUser;
using Atlas.Domain.Users;
using FluentValidation;

namespace Atlas.Application.Tests.Auth;

public class RefreshTokenTests
{
    private const string Password = "Current#Passw0rd1";

    private static async Task<(AppFixture App, User User, Atlas.Application.Auth.LoginResponse Login)> LoggedIn(UserRole role = UserRole.User)
    {
        var app = new AppFixture();
        var user = app.Users.Seed("john", role, password: Password);
        var login = await app.Send(new LoginCommand("john", Password));
        return (app, user, login);
    }

    [Fact]
    public async Task Login_issues_a_refresh_token_and_stores_only_its_hash()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;

        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
        Assert.NotNull(login.RefreshTokenExpiresAt);

        var stored = Assert.Single(app.RefreshTokens.Items);
        Assert.Equal(user.Id, stored.UserId);
        Assert.NotEqual(login.RefreshToken, stored.TokenHash);
        Assert.Equal(app.RefreshProvider.Hash(login.RefreshToken!), stored.TokenHash);
        Assert.Equal(user.SecurityStamp, stored.SecurityStamp);
    }

    [Fact]
    public async Task User_who_must_change_password_gets_no_refresh_token()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", mustChange: true, password: Password);

        var login = await app.Send(new LoginCommand("john", Password));

        Assert.True(login.MustChangePassword);
        Assert.Null(login.RefreshToken);
        Assert.Empty(app.RefreshTokens.Items);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_within_the_same_family()
    {
        var (app, _, login) = await LoggedIn();
        using var _ = app;
        app.Clock.Advance(TimeSpan.FromMinutes(5));

        var refreshed = await app.Send(new RefreshTokenCommand(login.RefreshToken!));

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.Equal("john", refreshed.User.Username);

        var (old, next) = (app.RefreshTokens.Items[0], app.RefreshTokens.Items[1]);
        Assert.True(old.IsRevoked);
        Assert.Equal(next.Id, old.ReplacedByTokenId);
        Assert.False(next.IsRevoked);
        Assert.Equal(old.FamilyId, next.FamilyId);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_session_family()
    {
        var (app, _, login) = await LoggedIn();
        using var _ = app;
        var second = await app.Send(new RefreshTokenCommand(login.RefreshToken!));

        // replay of the first token (e.g. a thief who copied it)
        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
        Assert.Equal("invalid_refresh_token", ex.Code);

        // the legitimate successor is dead as well
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(second.RefreshToken!)));
        Assert.All(app.RefreshTokens.Items, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Unknown_and_blank_tokens_are_rejected()
    {
        using var app = new AppFixture();

        var unknown = await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand("nope")));
        Assert.Equal("invalid_refresh_token", unknown.Code);
        await Assert.ThrowsAsync<ValidationException>(() => app.Send(new RefreshTokenCommand("")));
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var (app, _, login) = await LoggedIn();
        using var _ = app;
        app.Clock.Advance(TimeSpan.FromDays(8)); // default lifetime is 7 days

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
    }

    [Fact]
    public async Task Rotation_never_extends_a_session_past_its_absolute_lifetime()
    {
        using var app = new AppFixture();
        app.RefreshProvider.Lifetime = TimeSpan.FromDays(10);
        app.RefreshProvider.AbsoluteLifetime = TimeSpan.FromDays(15);
        app.Users.Seed("john", password: Password);
        var start = app.Clock.GetUtcNow();

        var login = await app.Send(new LoginCommand("john", Password));
        Assert.Equal(start.AddDays(10), login.RefreshTokenExpiresAt);

        app.Clock.Advance(TimeSpan.FromDays(9));
        var day9 = await app.Send(new RefreshTokenCommand(login.RefreshToken!));
        Assert.Equal(start.AddDays(15), day9.RefreshTokenExpiresAt); // capped, not day 19

        app.Clock.Advance(TimeSpan.FromDays(7)); // day 16: past the absolute limit
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(day9.RefreshToken!)));
    }

    [Fact]
    public async Task Deactivated_user_cannot_refresh()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;
        user.Deactivate(app.Clock.GetUtcNow());

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
        Assert.All(app.RefreshTokens.Items, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Security_stamp_change_invalidates_refresh_tokens()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;
        user.Rename("johnny", app.Clock.GetUtcNow()); // rotates the stamp

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
    }

    [Fact]
    public async Task Changing_password_revokes_old_sessions_and_returns_a_fresh_one()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;
        app.Current.SignInAs(user);
        const string newPassword = "Brand#New-Passw0rd";

        var changed = await app.Send(new ChangePasswordCommand(Password, newPassword, newPassword));

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
        Assert.False(string.IsNullOrWhiteSpace(changed.RefreshToken));
        var again = await app.Send(new RefreshTokenCommand(changed.RefreshToken!));
        Assert.NotEqual(changed.RefreshToken, again.RefreshToken);
    }

    [Fact]
    public async Task Admin_password_reset_revokes_the_users_refresh_tokens()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;
        app.SignInAsAdmin();

        await app.Send(new ResetUserPasswordCommand(user.Id));

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
    }

    [Fact]
    public async Task Role_change_revokes_the_users_refresh_tokens()
    {
        var (app, user, login) = await LoggedIn();
        using var _ = app;
        app.SignInAsAdmin();

        await app.Send(new UpdateUserCommand(user.Id, user.Username, UserRole.Admin));

        Assert.True(app.RefreshTokens.Items.Single().IsRevoked);
        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_is_silent_for_unknown_tokens()
    {
        var (app, _, login) = await LoggedIn();
        using var _ = app;

        await app.Send(new LogoutCommand(login.RefreshToken!));
        await app.Send(new LogoutCommand("not-a-real-token")); // must not throw or reveal anything

        await Assert.ThrowsAsync<UnauthorizedException>(() => app.Send(new RefreshTokenCommand(login.RefreshToken!)));
        Assert.All(app.RefreshTokens.Items, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Separate_logins_are_separate_sessions()
    {
        var (app, _, first) = await LoggedIn();
        using var _ = app;
        var second = await app.Send(new LoginCommand("john", Password));

        await app.Send(new LogoutCommand(first.RefreshToken!));

        // the other device keeps working
        var refreshed = await app.Send(new RefreshTokenCommand(second.RefreshToken!));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.RefreshToken));
    }
}
