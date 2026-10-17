using Atlas.Domain.Common;
using Atlas.Domain.Organization;
using Atlas.Domain.Users;

namespace Atlas.Domain.Tests.Organization;

public class OrganizationModelTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static User Ordinary() => User.Create("john", "hash", UserRole.User, false, T0);

    // ---------- users: optional, independent assignments ----------

    [Fact]
    public void New_user_is_an_orphan_with_no_department_and_no_profile()
    {
        var user = Ordinary();

        Assert.Null(user.DepartmentId);
        Assert.Null(user.ProfileId);
        Assert.True(user.IsOrphan);
        Assert.False(user.HasOrganizationalPrivileges);
    }

    [Fact]
    public void Department_and_profile_are_independent_of_each_other()
    {
        var user = Ordinary();

        user.AssignToDepartment(Guid.NewGuid(), T0);          // department without profile (administrative transition)
        Assert.False(user.IsOrphan);
        Assert.False(user.HasOrganizationalPrivileges);

        user.AssignProfile(Guid.NewGuid(), T0);                // now both
        Assert.True(user.HasOrganizationalPrivileges);

        user.RemoveProfile(T0);                                // profile gone, department stays
        Assert.NotNull(user.DepartmentId);
        Assert.False(user.HasOrganizationalPrivileges);

        user.RemoveFromDepartment(T0);
        Assert.True(user.IsOrphan);
    }

    [Fact]
    public void A_profile_can_be_held_without_a_department()
    {
        var user = Ordinary();
        user.AssignProfile(Guid.NewGuid(), T0);

        Assert.Null(user.DepartmentId);
        Assert.False(user.IsOrphan);               // has a profile
        Assert.False(user.HasOrganizationalPrivileges); // but no department => no organizational privileges
    }

    [Fact]
    public void Membership_is_exclusive_moving_replaces_the_department()
    {
        var user = Ordinary();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        user.AssignToDepartment(a, T0);
        user.AssignToDepartment(b, T0);

        Assert.Equal(b, user.DepartmentId); // a single column: never two departments at once
    }

    [Fact]
    public void Assignment_changes_do_not_rotate_the_security_stamp_or_touch_credentials()
    {
        var user = Ordinary();
        var stamp = user.SecurityStamp;
        var hash = user.PasswordHash;

        user.AssignToDepartment(Guid.NewGuid(), T0.AddMinutes(1));
        user.AssignProfile(Guid.NewGuid(), T0.AddMinutes(2));
        user.RemoveFromDepartment(T0.AddMinutes(3));

        Assert.Equal(stamp, user.SecurityStamp);
        Assert.Equal(hash, user.PasswordHash);
        Assert.Equal(T0.AddMinutes(3), user.UpdatedAt);
    }

    [Fact]
    public void Removing_what_is_not_there_is_a_no_op()
    {
        var user = Ordinary();
        user.RemoveFromDepartment(T0.AddMinutes(5));
        user.RemoveProfile(T0.AddMinutes(5));

        Assert.Equal(T0, user.UpdatedAt);
    }

    // ---------- administrators are outside the organization ----------

    [Fact]
    public void Administrator_accounts_cannot_receive_a_department_or_profile()
    {
        var admin = User.Create("root.admin", "hash", UserRole.Admin, false, T0);

        Assert.Throws<DomainException>(() => admin.AssignToDepartment(Guid.NewGuid(), T0));
        Assert.Throws<DomainException>(() => admin.AssignProfile(Guid.NewGuid(), T0));
        Assert.False(admin.IsOrphan); // never an "orphan": orphans are ordinary accounts
    }

    [Fact]
    public void Promoting_a_user_who_still_has_assignments_to_admin_is_rejected()
    {
        var user = Ordinary();
        user.AssignToDepartment(Guid.NewGuid(), T0);

        Assert.Throws<DomainException>(() => user.ChangeRole(UserRole.Admin, T0));

        user.RemoveFromDepartment(T0);
        user.ChangeRole(UserRole.Admin, T0); // fine once cleaned
        Assert.Equal(UserRole.Admin, user.Role);
    }

    // ---------- departments ----------

    [Fact]
    public void Department_is_valid_without_a_head_and_without_members()
    {
        var department = Department.Create("  Operations   North ", "desc", T0);

        Assert.Equal("Operations North", department.Name);
        Assert.Equal("OPERATIONS NORTH", department.NormalizedName);
        Assert.Null(department.HeadUserId);
    }

    [Fact]
    public void Head_can_be_appointed_replaced_and_removed_without_replacement()
    {
        var department = Department.Create("Ops", null, T0);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        department.AppointHead(first, T0);
        Assert.True(department.IsHead(first));

        department.AppointHead(second, T0);
        Assert.True(department.IsHead(second));
        Assert.False(department.IsHead(first));

        department.ClearHead(T0);
        Assert.Null(department.HeadUserId); // allowed: leaderless indefinitely
        department.ClearHead(T0);           // idempotent
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public void Invalid_department_and_profile_names_are_rejected(string name)
    {
        Assert.Throws<DomainException>(() => Department.Create(name, null, T0));
        Assert.Throws<DomainException>(() => Profile.Create(name, null, T0));
    }

    [Fact]
    public void Names_are_unique_case_and_whitespace_insensitively_by_normalization()
    {
        Assert.Equal(OrganizationNamePolicy.Normalize("Sales  North"), OrganizationNamePolicy.Normalize(" sales north "));
    }

    [Fact]
    public void Too_long_description_is_rejected() =>
        Assert.Throws<DomainException>(() => Profile.Create("Analyst", new string('x', OrganizationNamePolicy.MaxDescriptionLength + 1), T0));

    [Fact]
    public void Profile_can_be_renamed_without_any_user_involved()
    {
        var profile = Profile.Create("Analyst", null, T0);
        profile.Update("Senior Analyst", "more", T0.AddMinutes(1));

        Assert.Equal("Senior Analyst", profile.Name);
        Assert.Equal("SENIOR ANALYST", profile.NormalizedName);
        Assert.Equal(T0.AddMinutes(1), profile.UpdatedAt);
    }
}
