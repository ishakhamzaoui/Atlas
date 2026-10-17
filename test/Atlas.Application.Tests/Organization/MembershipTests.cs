using Atlas.Application.Common.Results;
using Atlas.Application.Departments.SetDepartmentHead;
using Atlas.Application.Organization.AssignUserToDepartment;
using Atlas.Application.Organization.RemoveUserFromDepartment;
using Atlas.Application.Profiles.AssignProfile;
using Atlas.Application.Tests.Support;
using Atlas.Application.Users.ListUsers;
using Atlas.Application.Users.UpdateUser;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Organization;

public class MembershipTests
{
    [Fact]
    public async Task Unassigned_user_joins_a_department_without_a_profile()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var user = app.Users.Seed("john");

        var change = await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id));

        Assert.Equal(dept.Id, change.User.DepartmentId);
        Assert.Null(change.User.ProfileId);         // allowed: department without profile (administrative transition)
        Assert.Null(change.PreviousDepartmentId);
        Assert.False(change.WasDepartmentHead);
        Assert.False(change.User.IsOrphan);
    }

    [Fact]
    public async Task User_can_join_a_department_and_receive_a_profile_in_one_call()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");

        var change = await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id, profile.Id));

        Assert.Equal(dept.Id, change.User.DepartmentId);
        Assert.Equal(profile.Id, change.User.ProfileId);
        Assert.True(user.HasOrganizationalPrivileges);
    }

    [Fact]
    public async Task Joining_from_no_department_keeps_an_existing_profile()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        await app.Ok(new AssignProfileCommand(user.Id, profile.Id));

        var change = await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id));

        Assert.Equal(profile.Id, change.User.ProfileId);
        Assert.Null(change.RemovedProfileId);
    }

    [Fact]
    public async Task Membership_is_exclusive_moving_ends_the_previous_membership()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Departments.Seed("Alpha");
        var b = app.Departments.Seed("Beta");
        var user = app.Users.Seed("john");

        await app.Ok(new AssignUserToDepartmentCommand(user.Id, a.Id));
        var move = await app.Ok(new AssignUserToDepartmentCommand(user.Id, b.Id));

        Assert.Equal(b.Id, user.DepartmentId);
        Assert.Equal(a.Id, move.PreviousDepartmentId);
        Assert.Empty((await app.Ok(new ListUsersQuery(1, 10, null, null, null, DepartmentId: a.Id))).Items);
        Assert.Single((await app.Ok(new ListUsersQuery(1, 10, null, null, null, DepartmentId: b.Id))).Items);
    }

    [Fact]
    public async Task Moving_between_departments_detaches_the_profile_but_retains_it()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Departments.Seed("Alpha");
        var b = app.Departments.Seed("Beta");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, a.Id, profile.Id));

        var move = await app.Ok(new AssignUserToDepartmentCommand(user.Id, b.Id));

        Assert.Null(move.User.ProfileId);                 // no access carried over from the former department
        Assert.Equal(profile.Id, move.RemovedProfileId);  // reported explicitly
        Assert.Contains(app.Profiles.Items, p => p.Id == profile.Id); // never deleted
        Assert.False(user.HasOrganizationalPrivileges);
    }

    [Fact]
    public async Task Moving_with_a_new_profile_in_the_same_call_replaces_the_old_one()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Departments.Seed("Alpha");
        var b = app.Departments.Seed("Beta");
        var oldProfile = app.Profiles.Seed("Analyst");
        var newProfile = app.Profiles.Seed("Auditor");
        var user = app.Users.Seed("john");
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, a.Id, oldProfile.Id));

        var move = await app.Ok(new AssignUserToDepartmentCommand(user.Id, b.Id, newProfile.Id));

        Assert.Equal(newProfile.Id, move.User.ProfileId);
        Assert.Equal(oldProfile.Id, move.RemovedProfileId);
        Assert.True(user.HasOrganizationalPrivileges);
    }

    [Fact]
    public async Task Moving_the_head_ends_leadership_without_requiring_a_replacement()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Departments.Seed("Alpha");
        var b = app.Departments.Seed("Beta");
        var head = app.Users.Seed("head");
        await app.Ok(new AssignUserToDepartmentCommand(head.Id, a.Id));
        await app.Ok(new SetDepartmentHeadCommand(a.Id, head.Id));

        var move = await app.Ok(new AssignUserToDepartmentCommand(head.Id, b.Id));

        Assert.True(move.WasDepartmentHead);
        Assert.Null(a.HeadUserId);              // Alpha is now headless - perfectly valid
        Assert.Null(b.HeadUserId);              // and the user does not become head of Beta
        Assert.Equal(b.Id, head.DepartmentId);
    }

    [Fact]
    public async Task Reassigning_to_the_same_department_changes_nothing_but_can_apply_a_profile()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id));
        await app.Ok(new SetDepartmentHeadCommand(dept.Id, user.Id));

        var same = await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id, profile.Id));

        Assert.False(same.WasDepartmentHead);          // not a move: leadership untouched
        Assert.Equal(user.Id, dept.HeadUserId);
        Assert.Equal(profile.Id, user.ProfileId);
    }

    [Fact]
    public async Task Removing_from_a_department_keeps_account_and_profile_but_drops_the_association()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id, profile.Id));

        var removed = await app.Ok(new RemoveUserFromDepartmentCommand(user.Id));

        Assert.Null(removed.User.DepartmentId);
        Assert.Null(removed.User.ProfileId);
        Assert.True(removed.User.IsOrphan);
        Assert.Equal(dept.Id, removed.PreviousDepartmentId);
        Assert.Equal(profile.Id, removed.RemovedProfileId);
        Assert.Contains(app.Users.Items, u => u.Id == user.Id);       // account untouched
        Assert.Contains(app.Profiles.Items, p => p.Id == profile.Id); // profile retained
    }

    [Fact]
    public async Task Removing_the_head_from_the_department_leaves_it_headless()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var head = app.Users.Seed("head");
        var member = app.Users.Seed("member");
        await app.Ok(new AssignUserToDepartmentCommand(head.Id, dept.Id));
        await app.Ok(new AssignUserToDepartmentCommand(member.Id, dept.Id));
        await app.Ok(new SetDepartmentHeadCommand(dept.Id, head.Id));

        var removed = await app.Ok(new RemoveUserFromDepartmentCommand(head.Id));

        Assert.True(removed.WasDepartmentHead);
        Assert.Null(dept.HeadUserId);
        Assert.Equal(dept.Id, member.DepartmentId); // the rest of the department is unaffected
    }

    [Fact]
    public async Task Removing_a_user_who_is_in_no_department_is_a_no_op()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");

        var change = await app.Ok(new RemoveUserFromDepartmentCommand(user.Id));

        Assert.Null(change.PreviousDepartmentId);
        Assert.False(change.WasDepartmentHead);
    }

    [Fact]
    public async Task Unknown_ids_are_not_found()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var user = app.Users.Seed("john");

        await app.Fail(new AssignUserToDepartmentCommand(Guid.NewGuid(), dept.Id), ErrorType.NotFound);
        await app.Fail(new AssignUserToDepartmentCommand(user.Id, Guid.NewGuid()), ErrorType.NotFound);
        await app.Fail(new AssignUserToDepartmentCommand(user.Id, dept.Id, Guid.NewGuid()), ErrorType.NotFound);
        await app.Fail(new RemoveUserFromDepartmentCommand(Guid.NewGuid()), ErrorType.NotFound);
        Assert.Null(user.DepartmentId); // a failed call changed nothing
    }

    [Fact]
    public async Task Admin_accounts_stay_outside_the_organization()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var member = app.Users.Seed("john");
        await app.Ok(new AssignUserToDepartmentCommand(member.Id, dept.Id));

        // an admin cannot be placed...
        var otherAdmin = app.Users.Seed("other.admin", UserRole.Admin);
        await app.Fail(new AssignUserToDepartmentCommand(otherAdmin.Id, dept.Id), ErrorType.BusinessRule, "admin_not_assignable");

        // ...and a user who is still organized cannot be promoted into the admin role
        await app.Fail(new UpdateUserCommand(member.Id, "john", UserRole.Admin), ErrorType.BusinessRule, "remove_organization_assignments_first");
        Assert.Equal(UserRole.User, member.Role);

        await app.Ok(new RemoveUserFromDepartmentCommand(member.Id));
        var promoted = await app.Ok(new UpdateUserCommand(member.Id, "john", UserRole.Admin));
        Assert.Equal("Admin", promoted.Role);
    }

    [Fact]
    public async Task Orphan_filter_lists_only_users_with_neither_department_nor_profile()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        app.Users.Seed("orphan1");
        var inDept = app.Users.Seed("indept");
        var withProfile = app.Users.Seed("withprofile");
        await app.Ok(new AssignUserToDepartmentCommand(inDept.Id, dept.Id));
        await app.Ok(new AssignProfileCommand(withProfile.Id, profile.Id));

        var orphans = await app.Ok(new ListUsersQuery(1, 10, null, null, null, Orphan: true));
        var others = await app.Ok(new ListUsersQuery(1, 10, null, null, null, Orphan: false));

        Assert.Equal("orphan1", Assert.Single(orphans.Items).Username); // the seeded admin is not an orphan
        Assert.Contains(others.Items, u => u.Username == "indept");
        Assert.Contains(others.Items, u => u.Username == "withprofile");
        Assert.DoesNotContain(others.Items, u => u.Username == "orphan1");
    }
}
