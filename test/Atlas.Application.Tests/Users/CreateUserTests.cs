using Atlas.Application.Common.Exceptions;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.CreateUser;
using Atlas.Domain.Users;
using FluentValidation;

namespace Atlas.Application.Tests.Users;

public class CreateUserTests
{
    private const string GoodPassword = "Initial#Passw0rd1";

    [Fact]
    public async Task Admin_creates_user_with_hashed_temporary_password()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var dto = await app.Send(new CreateUserCommand("JohnDoe", GoodPassword, UserRole.User));

        Assert.Equal("JohnDoe", dto.Username);
        Assert.Equal("User", dto.Role);
        Assert.True(dto.MustChangePassword);
        Assert.True(dto.IsActive);

        var stored = app.Users.Items.Single(u => u.Username == "JohnDoe");
        Assert.NotEqual(GoodPassword, stored.PasswordHash);
        Assert.Equal(FakePasswordHasher.HashOf(GoodPassword), stored.PasswordHash);
    }

    [Fact]
    public async Task Duplicate_username_differing_only_by_case_is_rejected()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        await app.Send(new CreateUserCommand("JohnDoe", GoodPassword, UserRole.User));

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            app.Send(new CreateUserCommand("johndoe", GoodPassword, UserRole.User)));

        Assert.Equal("username_taken", ex.Code);
    }

    [Theory]
    [InlineData("ab", GoodPassword)]
    [InlineData("john doe", GoodPassword)]
    [InlineData("john", "weak")]
    [InlineData("john", "NoSymbolsHere12345")]
    [InlineData("john", "My-john-Passw0rd!")] // contains username
    public async Task Invalid_input_fails_validation(string username, string password)
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Send(new CreateUserCommand(username, password, UserRole.User)));
        Assert.Single(app.Users.Items); // only the seeded admin
    }

    [Fact]
    public async Task Unknown_role_fails_validation()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        await Assert.ThrowsAsync<ValidationException>(() =>
            app.Send(new CreateUserCommand("john", GoodPassword, (UserRole)42)));
    }

    [Fact]
    public async Task Non_admin_is_forbidden()
    {
        using var app = new AppFixture();
        app.Current.SignInAs(app.Users.Seed("regular", UserRole.User));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            app.Send(new CreateUserCommand("john", GoodPassword, UserRole.User)));
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        using var app = new AppFixture();

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            app.Send(new CreateUserCommand("john", GoodPassword, UserRole.User)));
    }
}
