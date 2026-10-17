using Atlas.Application.Common.Results;
using Atlas.Application.Departments.ClearDepartmentHead;
using Atlas.Application.Departments.CreateDepartment;
using Atlas.Application.Departments.GetDepartment;
using Atlas.Application.Departments.ListDepartmentMembers;
using Atlas.Application.Departments.ListDepartments;
using Atlas.Application.Departments.SetDepartmentHead;
using Atlas.Application.Departments.UpdateDepartment;
using Atlas.Application.Organization.AssignUserToDepartment;
using Atlas.Application.Organization.RemoveUserFromDepartment;
using Atlas.Application.Profiles.AssignProfile;
using Atlas.Application.Profiles.CreateProfile;
using Atlas.Application.Profiles.GetProfile;
using Atlas.Application.Profiles.ListProfiles;
using Atlas.Application.Profiles.RemoveProfile;
using Atlas.Application.Profiles.UpdateProfile;
using Atlas.Application.Tests.Support;
using Atlas.Domain.Users;
using MediatR;

namespace Atlas.Application.Tests.Organization;

/// <summary>Only administrators manage users, profiles, departments, membership and heads. Everyone else is refused.</summary>
public class OrganizationAuthorizationTests
{
    private static IBaseRequest[] AllAdminOperations(Guid userId, Guid departmentId, Guid profileId) =>
    [
        new CreateProfileCommand("New", null),
        new UpdateProfileCommand(profileId, "Renamed", null),
        new GetProfileQuery(profileId),
        new ListProfilesQuery(1, 10, null),
        new AssignProfileCommand(userId, profileId),
        new RemoveProfileCommand(userId),
        new CreateDepartmentCommand("New Dept", null),
        new UpdateDepartmentCommand(departmentId, "Renamed", null),
        new GetDepartmentQuery(departmentId),
        new ListDepartmentsQuery(1, 10, null),
        new ListDepartmentMembersQuery(departmentId, 1, 10),
        new SetDepartmentHeadCommand(departmentId, userId),
        new ClearDepartmentHeadCommand(departmentId),
        new AssignUserToDepartmentCommand(userId, departmentId),
        new RemoveUserFromDepartmentCommand(userId)
    ];

    [Fact]
    public async Task Ordinary_users_including_department_heads_cannot_perform_any_organizational_operation()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var head = app.Users.Seed("head");
        head.AssignToDepartment(dept.Id, DateTimeOffset.UtcNow);
        head.AssignProfile(profile.Id, DateTimeOffset.UtcNow);
        dept.AppointHead(head.Id, DateTimeOffset.UtcNow);
        var target = app.Users.Seed("target");
        app.Current.SignInAs(head); // even the department head has no administrative authority

        foreach (var request in AllAdminOperations(target.Id, dept.Id, profile.Id))
        {
            var result = (Result)(await app.Get<ISender>().Send(request))!;
            AppFixture.AssertFailure(result, ErrorType.Forbidden, "forbidden");
        }

        Assert.Null(target.DepartmentId);
        Assert.Single(app.Departments.Items);
        Assert.Single(app.Profiles.Items);
    }

    [Fact]
    public async Task Anonymous_callers_are_unauthorized_for_every_operation()
    {
        using var app = new AppFixture();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var target = app.Users.Seed("target");

        foreach (var request in AllAdminOperations(target.Id, dept.Id, profile.Id))
        {
            var result = (Result)(await app.Get<ISender>().Send(request))!;
            AppFixture.AssertFailure(result, ErrorType.Unauthorized, "unauthenticated");
        }
    }

    [Fact]
    public async Task Authorization_is_checked_before_validation_and_lookups()
    {
        using var app = new AppFixture();
        app.Current.SignInAs(app.Users.Seed("regular", UserRole.User));

        // invalid input AND unknown ids: a non-admin learns nothing about either
        await app.Fail(new CreateDepartmentCommand("", null), ErrorType.Forbidden);
        await app.Fail(new SetDepartmentHeadCommand(Guid.NewGuid(), Guid.NewGuid()), ErrorType.Forbidden);
    }

    [Fact]
    public async Task Administrators_are_allowed()
    {
        using var app = new AppFixture();
        app.SignInAsAdmin();
        var dept = app.Departments.Seed("Ops");
        var profile = app.Profiles.Seed("Analyst");
        var target = app.Users.Seed("target");

        await app.Ok(new GetDepartmentQuery(dept.Id));
        await app.Ok(new GetProfileQuery(profile.Id));
        await app.Ok(new AssignUserToDepartmentCommand(target.Id, dept.Id, profile.Id));
        Assert.True(target.HasOrganizationalPrivileges);
    }
}
