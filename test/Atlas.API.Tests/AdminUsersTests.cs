using System.Net;
using System.Net.Http.Json;
using Atlas.API.Tests.Support;
using Atlas.Application.Common;
using Atlas.Application.Users;
using Atlas.Application.Users.ResetUserPassword;

namespace Atlas.API.Tests;

[Collection("api")]
public class AdminUsersTests(AtlasApiFactory factory)
{
    private const string Base = "/api/v1/admin/users";

    // ---------- authorization ----------

    [Fact]
    public async Task Anonymous_gets_401_on_every_admin_endpoint()
    {
        var client = factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Base}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Base, new { username = "x", password = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync($"{Base}/{id}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task Normal_user_gets_403_on_every_admin_endpoint()
    {
        var (_, token, _) = await factory.CreateReadyUserAsync("User");
        var client = factory.CreateClientWithToken(token);
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Base}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Base, new { username = AtlasApiFactory.UniqueUsername(), password = AtlasApiFactory.InitialUserPassword, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"{Base}/{id}", new { username = "abc", role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsync($"{Base}/{id}/status", JsonContent.Create(new { isActive = false }))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Base}/{id}/reset-password", null)).StatusCode);
    }

    [Fact]
    public async Task Normal_user_cannot_promote_themselves()
    {
        var (user, token, _) = await factory.CreateReadyUserAsync("User");
        var client = factory.CreateClientWithToken(token);

        var response = await client.PutAsJsonAsync($"{Base}/{user.Id}", new { username = user.Username, role = "Admin" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var admin = await factory.CreateAdminClientAsync();
        var current = await admin.GetFromJsonAsync<UserDto>($"{Base}/{user.Id}");
        Assert.Equal("User", current!.Role);
    }

    // ---------- create ----------

    [Fact]
    public async Task Admin_creates_user_and_response_hides_secrets()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = AtlasApiFactory.UniqueUsername("New");

        var response = await admin.PostAsJsonAsync(Base, new { username = name, password = AtlasApiFactory.InitialUserPassword, role = "User" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(AtlasApiFactory.InitialUserPassword, body);
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$2", body);

        var dto = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.True(dto.MustChangePassword);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task Duplicate_username_including_case_only_difference_is_409()
    {
        var (user, _) = await factory.CreateUserAsync(username: AtlasApiFactory.UniqueUsername("Dup"));
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync(Base,
            new { username = user.Username.ToUpperInvariant(), password = AtlasApiFactory.InitialUserPassword, role = "User" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("ab", "Initial#Passw0rd-2026")]
    [InlineData("valid.name", "weak")]
    [InlineData("bad name!", "Initial#Passw0rd-2026")]
    public async Task Invalid_create_requests_are_400_with_problem_details(string username, string password)
    {
        var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync(Base, new { username, password, role = "User" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Created_user_can_log_in_and_must_change_password()
    {
        var (user, password) = await factory.CreateUserAsync();
        var login = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, password);
        Assert.True(login.MustChangePassword);
    }

    // ---------- list / get ----------

    [Fact]
    public async Task List_supports_pagination_and_filtering()
    {
        var marker = AtlasApiFactory.UniqueUsername("lst");
        for (var i = 0; i < 3; i++) await factory.CreateUserAsync(username: $"{marker}{i}");
        var admin = await factory.CreateAdminClientAsync();

        var page1 = await admin.GetFromJsonAsync<PagedResult<UserDto>>($"{Base}?search={marker}&pageSize=2&page=1");
        var page2 = await admin.GetFromJsonAsync<PagedResult<UserDto>>($"{Base}?search={marker}&pageSize=2&page=2");

        Assert.Equal(3, page1!.TotalCount);
        Assert.Equal(2, page1.Items.Count);
        Assert.Single(page2!.Items);

        var admins = await admin.GetFromJsonAsync<PagedResult<UserDto>>($"{Base}?role=Admin&isActive=true&pageSize=100");
        Assert.All(admins!.Items, u => Assert.Equal("Admin", u.Role));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Base}?pageSize=1000")).StatusCode);
    }

    [Fact]
    public async Task Get_returns_404_for_unknown_id()
    {
        var admin = await factory.CreateAdminClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Base}/{Guid.NewGuid()}")).StatusCode);
    }

    // ---------- update / status ----------

    [Fact]
    public async Task Admin_can_update_username_and_role()
    {
        var (user, _) = await factory.CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        var newName = AtlasApiFactory.UniqueUsername("ren");

        var response = await admin.PutAsJsonAsync($"{Base}/{user.Id}", new { username = newName, role = "Admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal(newName, dto.Username);
        Assert.Equal("Admin", dto.Role);
    }

    [Fact]
    public async Task Rename_to_an_existing_username_is_409()
    {
        var (a, _) = await factory.CreateUserAsync();
        var (b, _) = await factory.CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PutAsJsonAsync($"{Base}/{b.Id}", new { username = a.Username.ToLowerInvariant(), role = "User" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_or_demote_themselves()
    {
        var admin = await factory.CreateAdminClientAsync();
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/v1/auth/me"))!;

        var deactivate = await admin.PatchAsync($"{Base}/{me.Id}/status", JsonContent.Create(new { isActive = false }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deactivate.StatusCode);

        var demote = await admin.PutAsJsonAsync($"{Base}/{me.Id}", new { username = me.Username, role = "User" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, demote.StatusCode);

        var still = await admin.GetFromJsonAsync<UserDto>($"{Base}/{me.Id}");
        Assert.True(still!.IsActive);
        Assert.Equal("Admin", still.Role);
    }

    [Fact]
    public async Task Deactivated_user_is_listed_inactive_and_can_be_reactivated()
    {
        var (user, password) = await factory.CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.PatchAsync($"{Base}/{user.Id}/status", JsonContent.Create(new { isActive = false }))).EnsureSuccessStatusCode();
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        (await admin.PatchAsync($"{Base}/{user.Id}/status", JsonContent.Create(new { isActive = true }))).EnsureSuccessStatusCode();
        Assert.False((await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, password)).User.Id == Guid.Empty);
    }

    // ---------- reset password ----------

    [Fact]
    public async Task Reset_password_returns_one_time_temporary_password_that_must_be_changed()
    {
        var (user, token, oldPassword) = await factory.CreateReadyUserAsync();
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsync($"{Base}/{user.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reset = (await response.Content.ReadFromJsonAsync<ResetPasswordResponse>())!;
        Assert.True(reset.MustChangePassword);
        Assert.NotEqual(oldPassword, reset.TemporaryPassword);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());

        // The user's old token and old password are dead.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClientWithToken(token).GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password = oldPassword })).StatusCode);

        // The temporary password works once and forces a change.
        var login = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, reset.TemporaryPassword);
        Assert.True(login.MustChangePassword);
    }

    [Fact]
    public async Task Admin_cannot_reset_their_own_password_through_the_admin_endpoint()
    {
        var admin = await factory.CreateAdminClientAsync();
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/v1/auth/me"))!;

        var response = await admin.PostAsync($"{Base}/{me.Id}/reset-password", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
