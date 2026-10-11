using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.API.Tests.Support;
using Atlas.Application.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Atlas.Infrastructure.Persistence;

namespace Atlas.API.Tests;

[Collection("api")]
public class RefreshEndpointsTests(AtlasApiFactory factory)
{
    private const string Refresh = "/api/v1/auth/refresh";
    private const string Logout = "/api/v1/auth/logout";

    private async Task<HttpResponseMessage> RefreshAsync(string token) =>
        await factory.CreateClient().PostAsJsonAsync(Refresh, new { refreshToken = token });

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task Login_after_password_change_returns_a_refresh_token()
    {
        var (_, session, _) = await factory.CreateReadySessionAsync();

        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.NotNull(session.RefreshTokenExpiresAt);
        Assert.True(session.RefreshTokenExpiresAt > DateTimeOffset.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task First_login_with_a_temporary_password_gets_no_refresh_token()
    {
        var (user, temp) = await factory.CreateUserAsync();

        var first = await AtlasApiFactory.LoginAsync(factory.CreateClient(), user.Username, temp);

        Assert.True(first.MustChangePassword);
        Assert.Null(first.RefreshToken);
    }

    [Fact]
    public async Task Refresh_returns_a_new_working_token_pair()
    {
        var (user, session, _) = await factory.CreateReadySessionAsync();

        var response = await RefreshAsync(session.RefreshToken!);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var next = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.NotEqual(session.RefreshToken, next.RefreshToken);
        Assert.False(next.MustChangePassword);

        var me = await factory.CreateClientWithToken(next.AccessToken).GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(user.Id, (await me.Content.ReadFromJsonAsync<Atlas.Application.Users.UserDto>())!.Id);
    }

    [Fact]
    public async Task Refresh_tokens_are_single_use_and_replay_kills_the_session()
    {
        var (_, session, _) = await factory.CreateReadySessionAsync();
        var second = (await (await RefreshAsync(session.RefreshToken!)).Content.ReadFromJsonAsync<LoginResponse>())!;

        var replay = await RefreshAsync(session.RefreshToken!);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("invalid_refresh_token", await ErrorCodeAsync(replay));

        // the legitimate holder of the successor is signed out too (theft response)
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(second.RefreshToken!)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_the_same_token_allow_at_most_one_winner()
    {
        var (_, session, _) = await factory.CreateReadySessionAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RefreshAsync(session.RefreshToken!)));

        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 1);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK),
            r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
    }

    [Fact]
    public async Task Garbage_tokens_are_401_and_blank_is_400()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync("definitely-not-a-token")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync("")).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session_and_is_204_even_for_unknown_tokens()
    {
        var (_, session, _) = await factory.CreateReadySessionAsync();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Logout, new { refreshToken = session.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Logout, new { refreshToken = "unknown" })).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken!)).StatusCode);
    }

    [Fact]
    public async Task Changing_password_invalidates_old_refresh_tokens_and_issues_a_new_one()
    {
        var (user, session, password) = await factory.CreateReadySessionAsync();
        var client = factory.CreateClientWithToken(session.AccessToken);
        const string newPassword = "Rotated#Passw0rd-2026-kT5";

        var change = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = password, newPassword, confirmNewPassword = newPassword });
        var changed = (await change.Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.False(string.IsNullOrWhiteSpace(changed.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(session.RefreshToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(changed.RefreshToken!)).StatusCode);
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Deactivation_and_admin_reset_end_refreshable_sessions()
    {
        var (deactivated, s1, _) = await factory.CreateReadySessionAsync();
        var (reset, s2, _) = await factory.CreateReadySessionAsync();
        var admin = await factory.CreateAdminClientAsync();

        (await admin.PatchAsync($"/api/v1/admin/users/{deactivated.Id}/status", JsonContent.Create(new { isActive = false }))).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/v1/admin/users/{reset.Id}/reset-password", null)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(s1.RefreshToken!)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RefreshAsync(s2.RefreshToken!)).StatusCode);
    }

    [Fact]
    public async Task Plaintext_refresh_tokens_are_never_stored()
    {
        var (_, session, _) = await factory.CreateReadySessionAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();

        Assert.False(await db.RefreshTokens.AnyAsync(t => t.TokenHash == session.RefreshToken));
        Assert.True(await db.RefreshTokens.AnyAsync(t => t.TokenHash.Length == 64));
    }
}
