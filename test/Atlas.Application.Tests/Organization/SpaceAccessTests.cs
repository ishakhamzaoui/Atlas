using Atlas.Application.Common.Results;
using Atlas.Application.Departments.SetDepartmentHead;
using Atlas.Application.Organization.AssignUserToDepartment;
using Atlas.Application.Organization.RemoveUserFromDepartment;
using Atlas.Application.Spaces.GetMySpaces;
using Atlas.Application.Spaces.GetSpace;
using Atlas.Application.Tests.Support;
using Atlas.Domain.Spaces;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Organization;

/// <summary>The three spaces always EXIST; what these tests pin down is who is AUTHORIZED to use them.</summary>
public class SpaceAccessTests
{
    private static (AppFixture App, User Orphan) OrphanSignedIn()
    {
        var app = new AppFixture();
        var orphan = app.Users.Seed("orphan");
        app.Current.SignInAs(orphan);
        return (app, orphan);
    }

    [Fact]
    public async Task Orphan_user_may_only_read_the_public_space()
    {
        var (app, orphan) = OrphanSignedIn();
        using var _ = app;

        var mine = await app.Ok(new GetMySpacesQuery());

        Assert.True(mine.IsOrphan);
        Assert.Null(mine.DepartmentId);
        Assert.Null(mine.Department);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));

        // existence is independent of authorization: the private space exists but is not usable yet
        Assert.True(mine.Private.Exists);
        Assert.Equal(orphan.Id, mine.Private.OwnerUserId);
        Assert.Empty(mine.Private.Access);

        var publicSpace = await app.Ok(new GetSpaceQuery(DataSpaceKind.Public, null));
        Assert.True(publicSpace.CanRead);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Private, orphan.Id), ErrorType.Forbidden);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, Guid.NewGuid()), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Member_with_a_profile_gets_own_private_and_department_spaces_but_not_others()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var other = app.Departments.Seed("Finance");
        var profile = app.Profiles.Seed("Analyst");
        var alice = app.Users.Seed("alice");
        var bob = app.Users.Seed("bob");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(alice.Id, dept.Id, profile.Id));
        await app.Ok(new AssignUserToDepartmentCommand(bob.Id, dept.Id, profile.Id));
        app.Current.SignInAs(alice);

        var mine = await app.Ok(new GetMySpacesQuery());
        Assert.False(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Private.Access));
        Assert.Equal("Read", Assert.Single(mine.Department!.Access));

        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Private, alice.Id))).CanRead);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Department, dept.Id))).CanRead);

        // same department does NOT open a colleague's private space; another department stays closed
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Private, bob.Id), ErrorType.Forbidden);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, other.Id), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Department_member_without_a_profile_has_no_organizational_access_yet()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var user = app.Users.Seed("john");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id)); // transition: department, no profile
        app.Current.SignInAs(user);

        var mine = await app.Ok(new GetMySpacesQuery());

        Assert.False(mine.IsOrphan);                   // has a department...
        Assert.Equal(dept.Id, mine.DepartmentId);
        Assert.True(mine.Department!.Exists);          // the department space exists...
        Assert.Empty(mine.Department.Access);          // ...but the user is not authorized to it yet
        Assert.Empty(mine.Private.Access);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, dept.Id), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Moving_a_user_revokes_access_to_the_former_department_immediately()
    {
        using var app = new AppFixture();
        var a = app.Departments.Seed("Alpha");
        var b = app.Departments.Seed("Beta");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, a.Id, profile.Id));
        app.Current.SignInAs(user);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Department, a.Id))).CanRead);

        app.SignInAsAdmin("second.admin");
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, b.Id));   // moved, profile detached
        app.Current.SignInAs(user);

        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, a.Id), ErrorType.Forbidden);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, b.Id), ErrorType.Forbidden); // no profile yet
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Private, user.Id), ErrorType.Forbidden);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Public, null))).CanRead);
    }

    [Fact]
    public async Task Removing_a_user_from_the_department_returns_them_to_public_only()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id, profile.Id));
        await app.Ok(new RemoveUserFromDepartmentCommand(user.Id));
        app.Current.SignInAs(user);

        var mine = await app.Ok(new GetMySpacesQuery());

        Assert.True(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        Assert.Empty(mine.Private.Access);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, dept.Id), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Department_space_works_the_same_with_or_without_a_head()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var head = app.Users.Seed("head");
        var member = app.Users.Seed("member");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(head.Id, dept.Id, profile.Id));
        await app.Ok(new AssignUserToDepartmentCommand(member.Id, dept.Id, profile.Id));

        app.Current.SignInAs(member);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Department, dept.Id))).CanRead); // headless

        app.SignInAsAdmin("second.admin");
        await app.Ok(new SetDepartmentHeadCommand(dept.Id, head.Id));
        app.Current.SignInAs(member);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Department, dept.Id))).CanRead); // with head: unchanged
        Assert.False((await app.Ok(new GetMySpacesQuery())).IsDepartmentHead);

        app.Current.SignInAs(head);
        Assert.True((await app.Ok(new GetMySpacesQuery())).IsDepartmentHead);
    }

    [Fact]
    public async Task Administrators_only_read_the_public_space()
    {
        using var app = new AppFixture();
        var admin = app.SignInAsAdmin();

        var mine = await app.Ok(new GetMySpacesQuery());

        Assert.False(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        Assert.Empty(mine.Private.Access);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Private, admin.Id), ErrorType.Forbidden);
        Assert.True((await app.Ok(new GetSpaceQuery(DataSpaceKind.Public, null))).CanRead);
    }

    [Fact]
    public async Task Anonymous_and_deactivated_callers_get_nothing_and_input_is_validated()
    {
        using var app = new AppFixture();
        await app.Fail(new GetMySpacesQuery(), ErrorType.Unauthorized);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Public, null), ErrorType.Unauthorized);

        var user = app.Users.Seed("john");
        app.Current.SignInAs(user);
        user.Deactivate(DateTimeOffset.UtcNow);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Public, null), ErrorType.Unauthorized);

        user.Activate(DateTimeOffset.UtcNow);
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Private, null), ErrorType.Validation); // owner id required
        await app.Fail(new GetSpaceQuery(DataSpaceKind.Department, Guid.Empty), ErrorType.Validation);
    }

    [Fact]
    public async Task Write_is_never_reported_as_granted()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");
        app.SignInAsAdmin();
        await app.Ok(new AssignUserToDepartmentCommand(user.Id, dept.Id, profile.Id));
        app.Current.SignInAs(user);

        var mine = await app.Ok(new GetMySpacesQuery());

        Assert.DoesNotContain("Write", mine.Private.Access);
        Assert.DoesNotContain("Write", mine.Department!.Access);
        Assert.DoesNotContain("Write", mine.Public.Access);
    }
}
