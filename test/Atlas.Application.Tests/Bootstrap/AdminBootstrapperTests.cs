using Atlas.Application.Bootstrap;
using Atlas.Application.Tests.Support;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Bootstrap;

public class AdminBootstrapperTests
{
    private const string Secret = "Bootstrap#Temp-2026x";

    [Fact]
    public async Task Creates_admin_that_must_change_password()
    {
        using var app = new AppFixture();

        var result = await app.Get<AdminBootstrapper>().RunAsync(Secret, default);

        Assert.Equal(BootstrapStatus.Created, result.Status);
        var admin = Assert.Single(app.Users.Items);
        Assert.Equal("admin", admin.Username);
        Assert.Equal(UserRole.Admin, admin.Role);
        Assert.True(admin.MustChangePassword);
        Assert.NotEqual(Secret, admin.PasswordHash);
    }

    [Fact]
    public async Task Second_run_is_a_no_op_and_never_resets_the_password()
    {
        using var app = new AppFixture();
        var bootstrapper = app.Get<AdminBootstrapper>();
        await bootstrapper.RunAsync(Secret, default);
        var admin = app.Users.Items.Single();
        admin.ChangePassword(FakePasswordHasher.HashOf("Permanent#Passw0rd9"), DateTimeOffset.UtcNow);
        var hashAfterChange = admin.PasswordHash;

        var result = await bootstrapper.RunAsync("Another#Secret-2026y", default);

        Assert.Equal(BootstrapStatus.AlreadyInitialized, result.Status);
        Assert.Single(app.Users.Items);
        Assert.Equal(hashAfterChange, admin.PasswordHash);
        Assert.False(admin.MustChangePassword);
    }

    [Fact]
    public async Task Does_nothing_once_any_admin_exists_even_under_another_name()
    {
        using var app = new AppFixture();
        app.Users.Seed("boss", UserRole.Admin);

        var result = await app.Get<AdminBootstrapper>().RunAsync(Secret, default);

        Assert.Equal(BootstrapStatus.AlreadyInitialized, result.Status);
        Assert.Single(app.Users.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_secret_creates_nothing(string? secret)
    {
        using var app = new AppFixture();

        var result = await app.Get<AdminBootstrapper>().RunAsync(secret, default);

        Assert.Equal(BootstrapStatus.MissingPassword, result.Status);
        Assert.Empty(app.Users.Items);
    }

    [Fact]
    public async Task Weak_secret_creates_nothing()
    {
        using var app = new AppFixture();

        var result = await app.Get<AdminBootstrapper>().RunAsync("password", default);

        Assert.Equal(BootstrapStatus.WeakPassword, result.Status);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(app.Users.Items);
    }

    [Fact]
    public async Task Existing_non_admin_named_admin_is_not_taken_over()
    {
        using var app = new AppFixture();
        var squatter = app.Users.Seed("Admin", UserRole.User);
        var hash = squatter.PasswordHash;

        var result = await app.Get<AdminBootstrapper>().RunAsync(Secret, default);

        Assert.Equal(BootstrapStatus.UsernameTaken, result.Status);
        Assert.Equal(UserRole.User, squatter.Role);
        Assert.Equal(hash, squatter.PasswordHash);
    }
}
