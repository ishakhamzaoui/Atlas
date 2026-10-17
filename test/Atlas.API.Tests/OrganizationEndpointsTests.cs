using System.Net;
using System.Net.Http.Json;
using Atlas.API.Tests.Support;
using Atlas.Application.Common;
using Atlas.Application.Departments;
using Atlas.Application.Organization;
using Atlas.Application.Profiles;
using Atlas.Application.Spaces;
using Atlas.Application.Users;

namespace Atlas.API.Tests;

[Collection("api")]
public class OrganizationEndpointsTests(AtlasApiFactory factory)
{
    private const string Admin = "/api/v1/admin";

    private async Task<MySpacesDto> MySpacesAsync(string token) =>
        (await factory.CreateClientWithToken(token).GetFromJsonAsync<MySpacesDto>("/api/v1/spaces/me"))!;

    private async Task<HttpStatusCode> StatusAsync(string token, string url) =>
        (await factory.CreateClientWithToken(token).GetAsync(url)).StatusCode;

    // ============================= authorization =============================

    [Fact]
    public async Task Ordinary_users_get_403_on_every_organization_admin_endpoint()
    {
        var (user, session, _) = await factory.CreateReadySessionAsync();
        var client = factory.CreateClientWithToken(session.AccessToken);
        var id = Guid.NewGuid();

        var responses = new[]
        {
            await client.PostAsJsonAsync($"{Admin}/departments", new { name = "Nope" }),
            await client.GetAsync($"{Admin}/departments"),
            await client.GetAsync($"{Admin}/departments/{id}"),
            await client.PutAsJsonAsync($"{Admin}/departments/{id}", new { name = "Nope" }),
            await client.GetAsync($"{Admin}/departments/{id}/members"),
            await client.PutAsJsonAsync($"{Admin}/departments/{id}/head", new { userId = user.Id }),
            await client.DeleteAsync($"{Admin}/departments/{id}/head"),
            await client.PostAsJsonAsync($"{Admin}/profiles", new { name = "Nope" }),
            await client.GetAsync($"{Admin}/profiles"),
            await client.GetAsync($"{Admin}/profiles/{id}"),
            await client.PutAsJsonAsync($"{Admin}/profiles/{id}", new { name = "Nope" }),
            await client.PutAsJsonAsync($"{Admin}/users/{user.Id}/department", new { departmentId = id }),
            await client.DeleteAsync($"{Admin}/users/{user.Id}/department"),
            await client.PutAsJsonAsync($"{Admin}/users/{user.Id}/profile", new { profileId = id }),
            await client.DeleteAsync($"{Admin}/users/{user.Id}/profile")
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }

    [Fact]
    public async Task Department_membership_does_not_make_someone_an_administrator()
    {
        var department = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (head, session, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(head.Id, department.Id, profile.Id);
        var admin = await factory.CreateAdminClientAsync();
        (await admin.PutAsJsonAsync($"{Admin}/departments/{department.Id}/head", new { userId = head.Id })).EnsureSuccessStatusCode();

        var response = await factory.CreateClientWithToken(session.AccessToken).PostAsJsonAsync($"{Admin}/departments", new { name = "Sneaky" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_get_401_and_restricted_first_login_tokens_are_refused_for_spaces()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/spaces/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/spaces/public")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Admin}/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Admin}/profiles")).StatusCode);

        var (user, temp) = await factory.CreateUserAsync();
        var first = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, temp);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(first.AccessToken, "/api/v1/spaces/public")); // must change password first
    }

    // ============================= departments & profiles =============================

    [Fact]
    public async Task Department_can_exist_with_no_members_and_no_head_and_names_are_unique()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = OrganizationTestHelpers.UniqueName("Ops");

        var created = await admin.PostAsJsonAsync($"{Admin}/departments", new { name, description = "d" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dept = (await created.Content.ReadFromJsonAsync<DepartmentDto>())!;
        Assert.Null(dept.HeadUserId);
        Assert.Equal(0, dept.MemberCount);

        var members = await admin.GetFromJsonAsync<PagedResult<DepartmentMemberDto>>($"{Admin}/departments/{dept.Id}/members");
        Assert.Empty(members!.Items);

        var duplicate = await admin.PostAsJsonAsync($"{Admin}/departments", new { name = name.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"{Admin}/departments", new { name = "" })).StatusCode);

        var renamed = await admin.PutAsJsonAsync($"{Admin}/departments/{dept.Id}", new { name = name + " Renamed" });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Admin}/departments/{Guid.NewGuid()}")).StatusCode);

        var list = await admin.GetFromJsonAsync<PagedResult<DepartmentDto>>($"{Admin}/departments?search={Uri.EscapeDataString(name)}");
        Assert.Single(list!.Items);
    }

    [Fact]
    public async Task Profiles_are_created_listed_renamed_and_names_are_unique()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = OrganizationTestHelpers.UniqueName("Analyst");

        var created = await admin.PostAsJsonAsync($"{Admin}/profiles", new { name });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var profile = (await created.Content.ReadFromJsonAsync<ProfileDto>())!;
        Assert.Equal(0, profile.AssignedUserCount);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"{Admin}/profiles", new { name = name.ToLowerInvariant() })).StatusCode);

        var unassigned = await admin.GetFromJsonAsync<PagedResult<ProfileDto>>($"{Admin}/profiles?assigned=false&search={Uri.EscapeDataString(name)}");
        Assert.Single(unassigned!.Items);

        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{Admin}/profiles/{profile.Id}", new { name = name + " II" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Admin}/profiles/{Guid.NewGuid()}")).StatusCode);
    }

    // ============================= orphan users =============================

    [Fact]
    public async Task Orphan_user_can_authenticate_but_only_read_the_public_space()
    {
        var (user, session, _) = await factory.CreateReadySessionAsync();   // no department, no profile
        Assert.True(user.IsOrphan);

        var mine = await MySpacesAsync(session.AccessToken);
        Assert.True(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        Assert.True(mine.Private.Exists);       // the space exists...
        Assert.Empty(mine.Private.Access);      // ...but the user may not use it
        Assert.Null(mine.Department);

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, "/api/v1/spaces/public"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/private/{user.Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{Guid.NewGuid()}"));
    }

    [Fact]
    public async Task Orphans_are_filterable_and_users_are_listed_with_their_organizational_state()
    {
        var (orphan, _, _) = await factory.CreateReadySessionAsync();
        var department = await factory.CreateDepartmentAsync();
        var (member, _, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(member.Id, department.Id);
        var admin = await factory.CreateAdminClientAsync();

        var orphans = await admin.GetFromJsonAsync<PagedResult<UserDto>>($"{Admin}/users?orphan=true&pageSize=100");
        Assert.Contains(orphans!.Items, u => u.Id == orphan.Id && u.IsOrphan);
        Assert.DoesNotContain(orphans.Items, u => u.Id == member.Id);

        var inDept = await admin.GetFromJsonAsync<PagedResult<UserDto>>($"{Admin}/users?departmentId={department.Id}");
        Assert.Equal(member.Id, Assert.Single(inDept!.Items).Id);
        Assert.Equal(department.Id, inDept.Items[0].DepartmentId);
    }

    // ============================= membership & access =============================

    [Fact]
    public async Task Assigning_department_and_profile_grants_access_immediately_with_the_same_token()
    {
        var department = await factory.CreateDepartmentAsync();
        var otherDepartment = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (user, session, _) = await factory.CreateReadySessionAsync();
        var (colleague, colleagueSession, _) = await factory.CreateReadySessionAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));

        await factory.AssignDepartmentAsync(user.Id, department.Id, profile.Id);
        await factory.AssignDepartmentAsync(colleague.Id, department.Id, profile.Id);

        var mine = await MySpacesAsync(session.AccessToken);   // same access token as before the assignment
        Assert.False(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Private.Access));
        Assert.Equal("Read", Assert.Single(mine.Department!.Access));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, $"/api/v1/spaces/private/{user.Id}"));

        // belonging to the same department does not open a colleague's private space, and other departments stay closed
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/private/{colleague.Id}"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{otherDepartment.Id}"));
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(colleagueSession.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));
    }

    [Fact]
    public async Task Department_without_a_profile_gives_no_organizational_access_until_a_profile_is_assigned()
    {
        var department = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (user, session, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(user.Id, department.Id);   // transition state: department, no profile

        var before = await MySpacesAsync(session.AccessToken);
        Assert.False(before.IsOrphan);
        Assert.True(before.Department!.Exists);
        Assert.Empty(before.Department.Access);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));

        var admin = await factory.CreateAdminClientAsync();
        (await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}/profile", new { profileId = profile.Id })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));
    }

    [Fact]
    public async Task Moving_a_user_detaches_the_profile_revokes_former_access_and_retains_the_profile()
    {
        var a = await factory.CreateDepartmentAsync();
        var b = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (user, session, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(user.Id, a.Id, profile.Id);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{a.Id}"));

        var move = await factory.AssignDepartmentAsync(user.Id, b.Id);

        Assert.Equal(a.Id, move.PreviousDepartmentId);
        Assert.Equal(profile.Id, move.RemovedProfileId);
        Assert.Null(move.User.ProfileId);
        Assert.Equal(b.Id, move.User.DepartmentId);
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{a.Id}")); // no lingering access
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{b.Id}")); // no profile yet

        var admin = await factory.CreateAdminClientAsync();
        var retained = await admin.GetFromJsonAsync<ProfileDto>($"{Admin}/profiles/{profile.Id}");
        Assert.Equal(0, retained!.AssignedUserCount);      // still exists, simply unassigned

        // membership is exclusive: the user is listed under b only
        var inA = await admin.GetFromJsonAsync<PagedResult<DepartmentMemberDto>>($"{Admin}/departments/{a.Id}/members");
        var inB = await admin.GetFromJsonAsync<PagedResult<DepartmentMemberDto>>($"{Admin}/departments/{b.Id}/members");
        Assert.DoesNotContain(inA!.Items, m => m.User.Id == user.Id);
        Assert.Contains(inB!.Items, m => m.User.Id == user.Id);

        // a profile can be given in the same call as a move
        var again = await factory.AssignDepartmentAsync(user.Id, a.Id, profile.Id);
        Assert.Equal(profile.Id, again.User.ProfileId);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.AccessToken, $"/api/v1/spaces/departments/{a.Id}"));
    }

    [Fact]
    public async Task Removing_a_user_from_a_department_keeps_the_account_and_returns_them_to_public_only()
    {
        var department = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (user, session, password) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(user.Id, department.Id, profile.Id);
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.DeleteAsync($"{Admin}/users/{user.Id}/department");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var change = (await response.Content.ReadFromJsonAsync<OrganizationChangeDto>())!;
        Assert.True(change.User.IsOrphan);
        Assert.Equal(profile.Id, change.RemovedProfileId);

        var mine = await MySpacesAsync(session.AccessToken);   // the account and its session still work
        Assert.True(mine.IsOrphan);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        Assert.Empty(mine.Private.Access);
        var relogin = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, password);
        Assert.False(relogin.MustChangePassword);

        var profiles = await admin.GetFromJsonAsync<PagedResult<ProfileDto>>($"{Admin}/profiles?assigned=false&pageSize=100");
        Assert.Contains(profiles!.Items, p => p.Id == profile.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"{Admin}/users/{user.Id}/department")).StatusCode); // idempotent
    }

    [Fact]
    public async Task Profile_can_be_assigned_replaced_and_removed_without_deleting_profiles()
    {
        var first = await factory.CreateProfileAsync();
        var second = await factory.CreateProfileAsync();
        var (user, _, _) = await factory.CreateReadySessionAsync();
        var admin = await factory.CreateAdminClientAsync();

        var assigned = await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}/profile", new { profileId = first.Id });
        Assert.Equal(first.Id, (await assigned.Content.ReadFromJsonAsync<UserDto>())!.ProfileId);
        var replaced = await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}/profile", new { profileId = second.Id });
        Assert.Equal(second.Id, (await replaced.Content.ReadFromJsonAsync<UserDto>())!.ProfileId);
        var removed = await admin.DeleteAsync($"{Admin}/users/{user.Id}/profile");
        Assert.Null((await removed.Content.ReadFromJsonAsync<UserDto>())!.ProfileId);

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Admin}/profiles/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Admin}/profiles/{second.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}/profile", new { profileId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"{Admin}/users/{Guid.NewGuid()}/department", new { departmentId = Guid.NewGuid() })).StatusCode);
    }

    // ============================= department heads =============================

    [Fact]
    public async Task Head_is_optional_must_be_a_member_and_removal_needs_no_replacement()
    {
        var department = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (alice, aliceSession, _) = await factory.CreateReadySessionAsync();
        var (bob, bobSession, _) = await factory.CreateReadySessionAsync();
        var (outsider, _, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(alice.Id, department.Id, profile.Id);
        await factory.AssignDepartmentAsync(bob.Id, department.Id, profile.Id);
        var admin = await factory.CreateAdminClientAsync();

        // a user outside the department cannot be head
        var rejected = await admin.PutAsJsonAsync($"{Admin}/departments/{department.Id}/head", new { userId = outsider.Id });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);

        // members can be: appoint, replace
        var appointed = await admin.PutAsJsonAsync($"{Admin}/departments/{department.Id}/head", new { userId = alice.Id });
        Assert.Equal(alice.Id, (await appointed.Content.ReadFromJsonAsync<DepartmentDto>())!.HeadUserId);
        var replaced = await admin.PutAsJsonAsync($"{Admin}/departments/{department.Id}/head", new { userId = bob.Id });
        Assert.Equal(bob.Id, (await replaced.Content.ReadFromJsonAsync<DepartmentDto>())!.HeadUserId);
        Assert.True((await MySpacesAsync(bobSession.AccessToken)).IsDepartmentHead);
        Assert.False((await MySpacesAsync(aliceSession.AccessToken)).IsDepartmentHead);

        // remove the head with no replacement: department and members keep working
        var cleared = await admin.DeleteAsync($"{Admin}/departments/{department.Id}/head");
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var dto = (await cleared.Content.ReadFromJsonAsync<DepartmentDto>())!;
        Assert.Null(dto.HeadUserId);
        Assert.Equal(2, dto.MemberCount);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(aliceSession.AccessToken, $"/api/v1/spaces/departments/{department.Id}"));
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"{Admin}/departments/{department.Id}/head")).StatusCode); // idempotent
    }

    [Fact]
    public async Task Moving_or_removing_the_head_leaves_the_department_headless_without_error()
    {
        var a = await factory.CreateDepartmentAsync();
        var b = await factory.CreateDepartmentAsync();
        var (head, _, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(head.Id, a.Id);
        var admin = await factory.CreateAdminClientAsync();
        (await admin.PutAsJsonAsync($"{Admin}/departments/{a.Id}/head", new { userId = head.Id })).EnsureSuccessStatusCode();

        var move = await factory.AssignDepartmentAsync(head.Id, b.Id);

        Assert.True(move.WasDepartmentHead);
        Assert.Null((await admin.GetFromJsonAsync<DepartmentDto>($"{Admin}/departments/{a.Id}"))!.HeadUserId);
        Assert.Null((await admin.GetFromJsonAsync<DepartmentDto>($"{Admin}/departments/{b.Id}"))!.HeadUserId);
    }

    // ============================= administrators are outside the organization =============================

    [Fact]
    public async Task Administrator_accounts_cannot_join_departments_or_hold_profiles_and_only_read_public()
    {
        var department = await factory.CreateDepartmentAsync();
        var profile = await factory.CreateProfileAsync();
        var (otherAdmin, _) = await factory.CreateUserAsync("Admin");
        var admin = await factory.CreateAdminClientAsync();

        var join = await admin.PutAsJsonAsync($"{Admin}/users/{otherAdmin.Id}/department", new { departmentId = department.Id });
        var hold = await admin.PutAsJsonAsync($"{Admin}/users/{otherAdmin.Id}/profile", new { profileId = profile.Id });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, join.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, hold.StatusCode);

        var session = await factory.GetAdminSessionAsync();
        var mine = await MySpacesAsync(session.Token);
        Assert.Equal("Read", Assert.Single(mine.Public.Access));
        Assert.Empty(mine.Private.Access);
        Assert.Equal(HttpStatusCode.OK, await StatusAsync(session.Token, "/api/v1/spaces/public"));
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(session.Token, $"/api/v1/spaces/departments/{department.Id}"));
    }

    [Fact]
    public async Task A_user_with_assignments_cannot_be_promoted_to_administrator_until_they_are_removed()
    {
        var department = await factory.CreateDepartmentAsync();
        var (user, _, _) = await factory.CreateReadySessionAsync();
        await factory.AssignDepartmentAsync(user.Id, department.Id);
        var admin = await factory.CreateAdminClientAsync();

        var blocked = await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}", new { username = user.Username, role = "Admin" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);

        (await admin.DeleteAsync($"{Admin}/users/{user.Id}/department")).EnsureSuccessStatusCode();
        var allowed = await admin.PutAsJsonAsync($"{Admin}/users/{user.Id}", new { username = user.Username, role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Invalid_organization_requests_are_clean_4xx_and_never_server_errors()
    {
        var admin = await factory.CreateAdminClientAsync();
        factory.Logs.Clear();

        var badHead = await admin.PutAsJsonAsync($"{Admin}/departments/{Guid.NewGuid()}/head", new { userId = Guid.Empty });
        var missingDept = await admin.PutAsJsonAsync($"{Admin}/users/{Guid.NewGuid()}/department", new { departmentId = Guid.Empty });
        var badPaging = await admin.GetAsync($"{Admin}/departments?pageSize=1000");

        Assert.Equal(HttpStatusCode.BadRequest, badHead.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingDept.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badPaging.StatusCode);
        Assert.DoesNotContain(factory.Logs.Events, e => e.Level >= Serilog.Events.LogEventLevel.Error);
    }
}
