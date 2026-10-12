using Atlas.Application.Auth.ChangePassword;
using Atlas.Application.Auth.Login;
using Atlas.Application.Auth.Refresh;
using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.CreateUser;
using Atlas.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Atlas.Application.Tests.Auth;

public class LoggingTests
{
    private const string Password = "Current#Passw0rd1";

    [Fact]
    public async Task Successful_request_logs_debug_trace_and_information_outcome()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);

        await app.Ok(new LoginCommand("john", Password));

        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Handling LoginCommand"));
        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Debug && e.Message.StartsWith("Handled LoginCommand"));
        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("logged in"));
        Assert.DoesNotContain(app.Logs.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Failed_login_logs_a_warning_with_the_reason_but_never_the_password()
    {
        using var app = new AppFixture();
        app.Users.Seed("john", password: Password);
        const string wrong = "Wrong#Passw0rd-SECRET";

        await app.Fail(new LoginCommand("john", wrong), ErrorType.Unauthorized);

        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("wrong_password"));
        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("rejected with invalid_credentials"));
        // Attempted usernames are DEBUG-only, never above.
        Assert.DoesNotContain(app.Logs.Entries, e => e.Level >= LogLevel.Information && e.Message.Contains("JOHN"));
    }

    [Fact]
    public async Task Expected_failures_are_warnings_without_exceptions_and_never_errors()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        app.Users.Seed("taken");

        // weak password, duplicate username, wrong login, unknown refresh token, invalid input: all perfectly normal outcomes
        await app.Fail(new CreateUserCommand("newbie", "weak", UserRole.User), ErrorType.Validation);
        await app.Fail(new CreateUserCommand("TAKEN", "Valid#Passw0rd-123", UserRole.User), ErrorType.Conflict);
        await app.Fail(new LoginCommand("ghost", Password), ErrorType.Unauthorized);
        await app.Fail(new RefreshTokenCommand("nope"), ErrorType.Unauthorized);

        Assert.DoesNotContain(app.Logs.Entries, e => e.Level >= LogLevel.Error);
        Assert.DoesNotContain(app.Logs.Entries, e => e.Exception is not null);
        Assert.Contains(app.Logs.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task No_log_entry_ever_contains_a_password_or_token()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        const string created = "Created#Passw0rd-AAA1";
        const string wrong = "Nope#Passw0rd-BBB2";
        const string newPassword = "Fresh#Passw0rd-CCC3";

        await app.Ok(new CreateUserCommand("newbie", created, UserRole.User));
        app.Current.SignInAs(app.Users.Items.Single(u => u.Username == "newbie"));
        await app.Fail(new ChangePasswordCommand(wrong, newPassword, newPassword), ErrorType.BusinessRule);
        await app.Ok(new ChangePasswordCommand(created, newPassword, newPassword));
        var login = await app.Ok(new LoginCommand("newbie", newPassword));

        foreach (var secret in new[] { created, wrong, newPassword, login.AccessToken, login.RefreshToken!, "hash:" })
            Assert.DoesNotContain(app.Logs.Entries, e => e.Message.Contains(secret) || (e.Exception?.ToString().Contains(secret) ?? false));
    }

    [Fact]
    public async Task Validation_failures_name_fields_not_values()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await app.Fail(new CreateUserCommand("ab", "weak-password-value", UserRole.User), ErrorType.Validation);

        var warning = Assert.Single(app.Logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("failed validation"));
        Assert.Contains("username", warning.Message);
        Assert.Contains("password", warning.Message);
        Assert.DoesNotContain("weak-password-value", warning.Message);
    }

    [Fact]
    public async Task Unexpected_failure_is_logged_once_as_error_with_the_exception()
    {
        using var app = new AppFixture();
        app.Users.FailLookupWith = () => new InvalidOperationException("database exploded");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => app.Send(new LoginCommand("john", Password)));

        var error = Assert.Single(app.Logs.Entries, e => e.Level == LogLevel.Error);
        Assert.Contains("LoginCommand failed unexpectedly", error.Message);
        Assert.Same(thrown, error.Exception);
        Assert.True(thrown.Data.Contains("atlas.logged")); // HTTP handler uses this to avoid a duplicate error entry
    }
}
