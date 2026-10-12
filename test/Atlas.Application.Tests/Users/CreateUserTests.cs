using Atlas.Application.Common.Results;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.CreateUser;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Users;

public class CreateUserTests
{
    private const string GoodPassword = "Initial#Passw0rd1";

    [Fact]
    public async Task Admin_creates_user_with_hashed_temporary_password()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var dto = await app.Ok(new CreateUserCommand("JohnDoe", GoodPassword, UserRole.User));

        Assert.Equal("JohnDoe", dto.Username);
        Assert.Equal("User", dto.Role);
        Assert.True(dto.MustChangePassword);
        Assert.True(dto.IsActive);

        var stored = app.Users.Items.Single(u => u.Username == "JohnDoe");
        Assert.NotEqual(GoodPassword, stored.PasswordHash);
        Assert.Equal(FakePasswordHasher.HashOf(GoodPassword), stored.PasswordHash);
    }

    [Fact]
    public async Task Duplicate_username_differing_only_by_case_is_a_conflict()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        await app.Ok(new CreateUserCommand("JohnDoe", GoodPassword, UserRole.User));

        await app.Fail(new CreateUserCommand("johndoe", GoodPassword, UserRole.User), ErrorType.Conflict, "username_taken");
    }

    [Theory]
    [InlineData("ab", GoodPassword)]
    [InlineData("john doe", GoodPassword)]
    [InlineData("john", "weak")]
    [InlineData("john", "NoSymbolsHere12345")]
    [InlineData("john", "My-john-Passw0rd!")] // contains username
    public async Task Invalid_input_returns_a_validation_failure_without_throwing(string username, string password)
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var error = await app.Fail(new CreateUserCommand(username, password, UserRole.User), ErrorType.Validation, "validation_failed");

        Assert.NotEmpty(error.ValidationErrors!);
        Assert.Single(app.Users.Items); // only the seeded admin
    }

    [Fact]
    public async Task Weak_password_reports_every_broken_rule_under_the_password_field()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var error = await app.Fail(new CreateUserCommand("john", "weak", UserRole.User), ErrorType.Validation);

        var messages = error.ValidationErrors!["password"];
        Assert.Contains(messages, m => m.Contains("at least 12"));
        Assert.Contains(messages, m => m.Contains("uppercase"));
        Assert.Contains(messages, m => m.Contains("digit"));
        Assert.Contains(messages, m => m.Contains("symbol"));
    }

    [Fact]
    public async Task Unknown_role_fails_validation()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await app.Fail(new CreateUserCommand("john", GoodPassword, (UserRole)42), ErrorType.Validation);
    }

    [Fact]
    public async Task Non_admin_is_forbidden()
    {
        using var app = new AppFixture();
        app.Current.SignInAs(app.Users.Seed("regular", UserRole.User));

        await app.Fail(new CreateUserCommand("john", GoodPassword, UserRole.User), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var app = new AppFixture();

        await app.Fail(new CreateUserCommand("john", GoodPassword, UserRole.User), ErrorType.Unauthorized, "unauthenticated");
    }

    [Fact]
    public async Task Authorization_is_checked_before_validation()
    {
        using var app = new AppFixture();
        app.Current.SignInAs(app.Users.Seed("regular", UserRole.User));

        // invalid AND forbidden: the caller learns nothing about validation rules
        await app.Fail(new CreateUserCommand("x", "weak", UserRole.User), ErrorType.Forbidden);
    }
}
