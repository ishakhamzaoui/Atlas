using Atlas.Domain.Common;
using Atlas.Domain.Users;

namespace Atlas.Domain.Tests.Users;

public class UserTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static User NewUser(string name = "JohnDoe", UserRole role = UserRole.User, bool mustChange = true) =>
        User.Create(name, "hash-1", role, mustChange, T0);

    [Fact]
    public void Create_sets_defaults_and_normalizes_username()
    {
        var user = NewUser("JohnDoe");

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("JohnDoe", user.Username);
        Assert.Equal("JOHNDOE", user.NormalizedUsername);
        Assert.True(user.IsActive);
        Assert.True(user.MustChangePassword);
        Assert.Equal(T0, user.CreatedAt);
        Assert.NotEqual(Guid.Empty, user.SecurityStamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("bad name")]
    public void Create_rejects_invalid_username(string name) =>
        Assert.Throws<DomainException>(() => NewUser(name));

    [Fact]
    public void Create_rejects_empty_hash_and_unknown_role()
    {
        Assert.Throws<DomainException>(() => User.Create("john", "", UserRole.User, true, T0));
        Assert.Throws<DomainException>(() => User.Create("john", "h", (UserRole)99, true, T0));
    }

    [Fact]
    public void ChangePassword_clears_flag_updates_hash_and_rotates_stamp()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;

        user.ChangePassword("hash-2", T0.AddHours(1));

        Assert.False(user.MustChangePassword);
        Assert.Equal("hash-2", user.PasswordHash);
        Assert.Equal(T0.AddHours(1), user.PasswordChangedAt);
        Assert.NotEqual(stamp, user.SecurityStamp);
    }

    [Fact]
    public void SetTemporaryPassword_forces_change_and_rotates_stamp()
    {
        var user = NewUser(mustChange: false);
        var stamp = user.SecurityStamp;

        user.SetTemporaryPassword("temp-hash", T0.AddHours(1));

        Assert.True(user.MustChangePassword);
        Assert.Equal("temp-hash", user.PasswordHash);
        Assert.NotEqual(stamp, user.SecurityStamp);
    }

    [Fact]
    public void Deactivate_rotates_stamp_and_is_idempotent()
    {
        var user = NewUser();
        user.Deactivate(T0.AddMinutes(1));
        var stamp = user.SecurityStamp;
        user.Deactivate(T0.AddMinutes(2));

        Assert.False(user.IsActive);
        Assert.Equal(stamp, user.SecurityStamp);
        Assert.Equal(T0.AddMinutes(1), user.UpdatedAt);
    }

    [Fact]
    public void Activate_restores_account()
    {
        var user = NewUser();
        user.Deactivate(T0);
        user.Activate(T0.AddMinutes(5));
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Rename_updates_normalized_username_and_validates()
    {
        var user = NewUser("john");
        user.Rename("Johnny", T0.AddMinutes(1));

        Assert.Equal("Johnny", user.Username);
        Assert.Equal("JOHNNY", user.NormalizedUsername);
        Assert.Throws<DomainException>(() => user.Rename("x", T0));
    }

    [Fact]
    public void ChangeRole_changes_role_and_rotates_stamp()
    {
        var user = NewUser();
        var stamp = user.SecurityStamp;
        user.ChangeRole(UserRole.Admin, T0.AddMinutes(1));

        Assert.Equal(UserRole.Admin, user.Role);
        Assert.NotEqual(stamp, user.SecurityStamp);
    }
}
