using Atlas.Application.Common.Results;
using Atlas.Application.Departments.ClearDepartmentHead;
using Atlas.Application.Departments.CreateDepartment;
using Atlas.Application.Departments.GetDepartment;
using Atlas.Application.Departments.ListDepartmentMembers;
using Atlas.Application.Departments.ListDepartments;
using Atlas.Application.Departments.SetDepartmentHead;
using Atlas.Application.Departments.UpdateDepartment;
using Atlas.Application.Organization.AssignUserToDepartment;
using Atlas.Application.Tests.Support;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Organization;

public class DepartmentManagementTests
{
    [Fact]
    public async Task Department_can_be_created_with_no_members_and_no_head()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var dto = await app.Ok(new CreateDepartmentCommand("Operations", "ops"));

        Assert.Equal("Operations", dto.Name);
        Assert.Null(dto.HeadUserId);
        Assert.Equal(0, dto.MemberCount);

        var members = await app.Ok(new ListDepartmentMembersQuery(dto.Id, 1, 10));
        Assert.Empty(members.Items);
        Assert.Equal(0, members.TotalCount);
    }

    [Fact]
    public async Task Duplicate_department_name_is_a_conflict_and_updates_cannot_collide()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        await app.Ok(new CreateDepartmentCommand("Operations", null));
        var other = app.Departments.Seed("Finance");

        await app.Fail(new CreateDepartmentCommand(" operations ", null), ErrorType.Conflict, "department_name_taken");
        await app.Fail(new UpdateDepartmentCommand(other.Id, "OPERATIONS", null), ErrorType.Conflict, "department_name_taken");

        var renamed = await app.Ok(new UpdateDepartmentCommand(other.Id, "Finance & Audit", "x"));
        Assert.Equal("Finance & Audit", renamed.Name);
        await app.Fail(new UpdateDepartmentCommand(Guid.NewGuid(), "Nope", null), ErrorType.NotFound);
    }

    [Fact]
    public async Task Head_must_be_a_member_of_the_department_they_lead()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var other = app.Departments.Seed("Finance");
        var outsider = app.Users.Seed("outsider");               // no department at all
        var elsewhere = app.Users.Seed("elsewhere");
        elsewhere.AssignToDepartment(other.Id, DateTimeOffset.UtcNow); // member of ANOTHER department

        await app.Fail(new SetDepartmentHeadCommand(dept.Id, outsider.Id), ErrorType.BusinessRule, "head_must_be_department_member");
        await app.Fail(new SetDepartmentHeadCommand(dept.Id, elsewhere.Id), ErrorType.BusinessRule, "head_must_be_department_member");
        Assert.Null(dept.HeadUserId);
    }

    [Fact]
    public async Task Head_can_be_appointed_replaced_and_removed_without_any_replacement()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var alice = app.Users.Seed("alice");
        var bob = app.Users.Seed("bob");
        await app.Ok(new AssignUserToDepartmentCommand(alice.Id, dept.Id));
        await app.Ok(new AssignUserToDepartmentCommand(bob.Id, dept.Id));

        var appointed = await app.Ok(new SetDepartmentHeadCommand(dept.Id, alice.Id));
        Assert.Equal(alice.Id, appointed.HeadUserId);
        Assert.Equal("alice", appointed.HeadUsername);

        var replaced = await app.Ok(new SetDepartmentHeadCommand(dept.Id, bob.Id));
        Assert.Equal(bob.Id, replaced.HeadUserId);
        Assert.Equal(dept.Id, alice.DepartmentId); // the former head simply stays a member

        var cleared = await app.Ok(new ClearDepartmentHeadCommand(dept.Id));
        Assert.Null(cleared.HeadUserId);              // no replacement required
        Assert.Equal(2, cleared.MemberCount);         // department keeps functioning with its members
        await app.Ok(new ClearDepartmentHeadCommand(dept.Id)); // idempotent
    }

    [Fact]
    public async Task Members_listing_flags_the_head_and_works_for_headless_departments()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var alice = app.Users.Seed("alice");
        var bob = app.Users.Seed("bob");
        await app.Ok(new AssignUserToDepartmentCommand(alice.Id, dept.Id));
        await app.Ok(new AssignUserToDepartmentCommand(bob.Id, dept.Id));

        var headless = await app.Ok(new ListDepartmentMembersQuery(dept.Id, 1, 10));
        Assert.Equal(2, headless.TotalCount);
        Assert.All(headless.Items, m => Assert.False(m.IsHead));

        await app.Ok(new SetDepartmentHeadCommand(dept.Id, bob.Id));
        var withHead = await app.Ok(new ListDepartmentMembersQuery(dept.Id, 1, 10));
        Assert.Equal("bob", Assert.Single(withHead.Items, m => m.IsHead).User.Username);
    }

    [Fact]
    public async Task Listing_and_getting_departments_includes_member_counts()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Departments.Seed("Alpha");
        app.Departments.Seed("Beta");
        await app.Ok(new AssignUserToDepartmentCommand(app.Users.Seed("user1").Id, a.Id));

        var page = await app.Ok(new ListDepartmentsQuery(1, 10, null));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.Items.Single(d => d.Name == "Alpha").MemberCount);

        var search = await app.Ok(new ListDepartmentsQuery(1, 10, "bet"));
        Assert.Equal("Beta", Assert.Single(search.Items).Name);

        Assert.Equal(1, (await app.Ok(new GetDepartmentQuery(a.Id))).MemberCount);
        await app.Fail(new GetDepartmentQuery(Guid.NewGuid()), ErrorType.NotFound);
        await app.Fail(new ListDepartmentMembersQuery(Guid.NewGuid(), 1, 10), ErrorType.NotFound);
    }

    [Fact]
    public async Task Unknown_ids_are_not_found()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var user = app.Users.Seed("john");

        await app.Fail(new SetDepartmentHeadCommand(Guid.NewGuid(), user.Id), ErrorType.NotFound);
        await app.Fail(new SetDepartmentHeadCommand(dept.Id, Guid.NewGuid()), ErrorType.NotFound);
        await app.Fail(new ClearDepartmentHeadCommand(Guid.NewGuid()), ErrorType.NotFound);
    }

    [Fact]
    public async Task Administrator_accounts_cannot_be_department_members_or_heads()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var otherAdmin = app.Users.Seed("other.admin", UserRole.Admin);

        await app.Fail(new AssignUserToDepartmentCommand(otherAdmin.Id, dept.Id), ErrorType.BusinessRule, "admin_not_assignable");
        await app.Fail(new SetDepartmentHeadCommand(dept.Id, otherAdmin.Id), ErrorType.BusinessRule, "head_must_be_department_member");
    }
}
