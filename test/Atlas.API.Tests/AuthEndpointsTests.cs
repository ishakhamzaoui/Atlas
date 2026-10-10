using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.API.Tests.Support;
using Atlas.Application.Auth;
using Atlas.Application.Users;

namespace Atlas.API.Tests;

[Collection("api")]
public class AuthEndpointsTests(AtlasApiFactory factory)
{
    [Fact]
    public async Task Bootstrap_admin_must_change_password_on_first_login()
    {
        var session = await factory.GetAdminSessionAsync();

        Assert.True(session.FirstLogin.MustChangePassword);
        Assert.Equal("Admin", session.FirstLogin.User.Role);
        Assert.Equal("admin", session.FirstLogin.User.Username);

        // After the change the admin logs in with the permanent password and is unrestricted.
        var again = await AtlasApiFactory.LoginAsync(factory.CreateClient(), "admin", AtlasApiFactory.AdminPermanentPassword);
        Assert.False(again.MustChangePassword);

        // The bootstrap password no longer works.
        var old = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { username = "admin", password = AtlasApiFactory.BootstrapPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
    }

    [Fact]
    public async Task Login_succeeds_and_returns_token_metadata()
    {
        var (user, password) = await factory.CreateUserAsync();

        var login = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username.ToUpperInvariant(), password);

        Assert.Equal("Bearer", login.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.InRange(login.ExpiresIn, 1, 15 * 60);
        Assert.True(login.MustChangePassword);
    }

    [Fact]
    public async Task Failed_logins_are_generic_and_identical()
    {
        var (user, password) = await factory.CreateUserAsync();
        var client = factory.CreateClient();

        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "nobody-" + Guid.NewGuid().ToString("N")[..8], password });
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password = "Wrong#Passw0rd-1" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var a = await unknown.Content.ReadAsStringAsync();
        var b = await wrong.Content.ReadAsStringAsync();
        Assert.Equal(Normalize(a), Normalize(b));
    }

    [Fact]
    public async Task Inactive_account_cannot_log_in()
    {
        var (user, password) = await factory.CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        var off = await admin.PatchAsJsonAsync($"/api/v1/admin/users/{user.Id}/status", new { isActive = false });
        off.EnsureSuccessStatusCode();

        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_with_missing_fields_is_a_validation_error()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = "", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Forced_password_change_restricts_the_token_until_the_password_is_changed()
    {
        var (user, temp) = await factory.CreateUserAsync("Admin"); // an admin-role account on first login
        var first = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, temp);
        var restricted = factory.CreateClientWithToken(first.AccessToken);

        // Admin role, but pending password change => no access to normal functionality.
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/v1/admin/users")).StatusCode);
        // ...but /me and change-password are available.
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/v1/auth/me")).StatusCode);

        const string permanent = "Permanent#Changed-2026-pR8";
        var change = await restricted.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = temp, newPassword = permanent, confirmNewPassword = permanent });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(changed.MustChangePassword);

        // The restricted token is dead (stamp rotated); the fresh one is unrestricted.
        Assert.Equal(HttpStatusCode.Unauthorized, (await restricted.GetAsync("/api/v1/auth/me")).StatusCode);
        var full = factory.CreateClientWithToken(changed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await full.GetAsync("/api/v1/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Change_password_rejects_wrong_current_weak_new_and_mismatched_confirmation()
    {
        var (user, temp) = await factory.CreateUserAsync();
        var first = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, temp);
        var client = factory.CreateClientWithToken(first.AccessToken);
        const string good = "Another#Passw0rd-2026";

        var wrongCurrent = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "Wrong#Passw0rd-1", newPassword = good, confirmNewPassword = good });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongCurrent.StatusCode);

        var weak = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = temp, newPassword = "weak", confirmNewPassword = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        var mismatch = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = temp, newPassword = good, confirmNewPassword = good + "x" });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);

        // Still must change: the failed attempts changed nothing.
        var again = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, temp);
        Assert.True(again.MustChangePassword);
    }

    [Fact]
    public async Task Me_returns_a_safe_profile_without_secrets()
    {
        var (user, token, _) = await factory.CreateReadyUserAsync();
        var response = await factory.CreateClientWithToken(token).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        var dto = JsonSerializer.Deserialize<UserDto>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(user.Id, dto.Id);
        Assert.False(dto.MustChangePassword);
    }

    [Fact]
    public async Task Responses_are_not_cacheable()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = "x", password = "y" });
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
    }

    // Both bodies target the same path, so they must be byte-for-byte identical.
    private static string Normalize(string json) => json;
}

internal static class HttpClientPatchExtensions
{
    public static Task<HttpResponseMessage> PatchAsJsonAsync<T>(this HttpClient client, string url, T value) =>
        client.PatchAsync(url, JsonContent.Create(value));
}
