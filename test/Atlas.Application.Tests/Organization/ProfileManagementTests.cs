using Atlas.Application.Common.Results;
using Atlas.Application.Profiles.AssignProfile;
using Atlas.Application.Profiles.CreateProfile;
using Atlas.Application.Profiles.GetProfile;
using Atlas.Application.Profiles.ListProfiles;
using Atlas.Application.Profiles.RemoveProfile;
using Atlas.Application.Profiles.UpdateProfile;
using Atlas.Application.Tests.Support;
using Atlas.Domain.Users;

namespace Atlas.Application.Tests.Organization;

public class ProfileManagementTests
{
    [Fact]
    public async Task Admin_creates_a_profile_that_starts_unassigned()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();

        var dto = await app.Ok(new CreateProfileCommand("  Analyst ", "reads reports"));

        Assert.Equal("Analyst", dto.Name);
        Assert.Equal(0, dto.AssignedUserCount);
        Assert.Single(app.Profiles.Items);
    }

    [Fact]
    public async Task Duplicate_profile_name_is_a_conflict_even_with_different_case_or_spacing()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        await app.Ok(new CreateProfileCommand("Senior Analyst", null));

        await app.Fail(new CreateProfileCommand(" senior   analyst ", null), ErrorType.Conflict, "profile_name_taken");
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    public async Task Invalid_profile_names_fail_validation(string name)
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        await app.Fail(new CreateProfileCommand(name, null), ErrorType.Validation);
    }

    [Fact]
    public async Task Admin_can_rename_a_profile_but_not_to_an_existing_name()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var a = app.Profiles.Seed("Analyst");
        app.Profiles.Seed("Auditor");

        var renamed = await app.Ok(new UpdateProfileCommand(a.Id, "Lead Analyst", "x"));
        Assert.Equal("Lead Analyst", renamed.Name);

        await app.Fail(new UpdateProfileCommand(a.Id, "AUDITOR", null), ErrorType.Conflict, "profile_name_taken");
        await app.Fail(new UpdateProfileCommand(Guid.NewGuid(), "Whatever", null), ErrorType.NotFound);
    }

    [Fact]
    public async Task Assigning_replacing_and_removing_a_profile_never_deletes_any_profile()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");
        var first = app.Profiles.Seed("Analyst");
        var second = app.Profiles.Seed("Auditor");

        var assigned = await app.Ok(new AssignProfileCommand(user.Id, first.Id));
        Assert.Equal(first.Id, assigned.ProfileId);

        var replaced = await app.Ok(new AssignProfileCommand(user.Id, second.Id));
        Assert.Equal(second.Id, replaced.ProfileId);

        var removed = await app.Ok(new RemoveProfileCommand(user.Id));
        Assert.Null(removed.ProfileId);

        Assert.Equal(2, app.Profiles.Items.Count); // both retained
        var unassigned = await app.Ok(new ListProfilesQuery(1, 10, null, Assigned: false));
        Assert.Equal(2, unassigned.TotalCount);
    }

    [Fact]
    public async Task Profile_assignment_is_independent_of_department_membership()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");
        var department = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");

        var withProfileOnly = await app.Ok(new AssignProfileCommand(user.Id, profile.Id));
        Assert.Null(withProfileOnly.DepartmentId);   // assigning a profile did not touch the department

        user.AssignToDepartment(department.Id, DateTimeOffset.UtcNow);
        await app.Ok(new RemoveProfileCommand(user.Id));
        Assert.Equal(department.Id, user.DepartmentId); // removing a profile did not touch the department
    }

    [Fact]
    public async Task One_profile_can_be_held_by_several_users_and_counts_reflect_it()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var profile = app.Profiles.Seed("Analyst");
        var a = app.Users.Seed("alice");
        var b = app.Users.Seed("bob");

        await app.Ok(new AssignProfileCommand(a.Id, profile.Id));
        await app.Ok(new AssignProfileCommand(b.Id, profile.Id));

        var dto = await app.Ok(new GetProfileQuery(profile.Id));
        Assert.Equal(2, dto.AssignedUserCount);
        var assigned = await app.Ok(new ListProfilesQuery(1, 10, null, Assigned: true));
        Assert.Single(assigned.Items);
    }

    [Fact]
    public async Task Unknown_user_or_profile_is_not_found_and_admins_cannot_hold_profiles()
    {
        using var app = new AppFixture();
        var admin = app.SignInAsAdmin();
        var other = app.Users.Seed("other.admin", UserRole.Admin);
        var profile = app.Profiles.Seed("Analyst");
        var user = app.Users.Seed("john");

        await app.Fail(new AssignProfileCommand(Guid.NewGuid(), profile.Id), ErrorType.NotFound);
        await app.Fail(new AssignProfileCommand(user.Id, Guid.NewGuid()), ErrorType.NotFound);
        await app.Fail(new AssignProfileCommand(other.Id, profile.Id), ErrorType.BusinessRule, "admin_not_assignable");
        await app.Fail(new RemoveProfileCommand(Guid.NewGuid()), ErrorType.NotFound);
        Assert.Null(admin.ProfileId);
    }

    [Fact]
    public async Task Removing_a_profile_from_a_user_without_one_is_a_harmless_no_op()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var user = app.Users.Seed("john");

        var dto = await app.Ok(new RemoveProfileCommand(user.Id));
        Assert.Null(dto.ProfileId);
    }
}
