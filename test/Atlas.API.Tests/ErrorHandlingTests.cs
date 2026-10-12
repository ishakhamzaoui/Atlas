using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atlas.API.Tests.Support;
using Serilog.Events;

namespace Atlas.API.Tests;

/// <summary>Expected failures (weak password, duplicate username, bad credentials...) are normal: clean 4xx, no error logs, no stack traces.</summary>
[Collection("api")]
public class ErrorHandlingTests(AtlasApiFactory factory)
{
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private void AssertNoErrorsLogged()
    {
        Assert.DoesNotContain(factory.Logs.Events, e => e.Level >= LogEventLevel.Error);
        Assert.DoesNotContain(factory.Logs.Events, e => e.Exception is not null);
    }

    [Fact]
    public async Task Weak_password_is_a_400_with_field_errors_and_nothing_is_logged_as_an_error()
    {
        var admin = await factory.CreateAdminClientAsync();
        factory.Logs.Clear();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new { username = AtlasApiFactory.UniqueUsername(), password = "weak", role = "User" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        var messages = body.GetProperty("errors").GetProperty("password").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(messages, m => m!.Contains("at least 12"));
        Assert.Contains(messages, m => m!.Contains("uppercase"));

        AssertNoErrorsLogged();
    }

    [Fact]
    public async Task Duplicate_username_is_a_409_and_not_logged_as_an_error()
    {
        var (user, _) = await factory.CreateUserAsync();
        var admin = await factory.CreateAdminClientAsync();
        factory.Logs.Clear();

        var response = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new { username = user.Username.ToUpperInvariant(), password = AtlasApiFactory.InitialUserPassword, role = "User" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("username_taken", (await ReadAsync(response)).GetProperty("code").GetString());
        AssertNoErrorsLogged();
    }

    [Fact]
    public async Task Bad_credentials_wrong_current_password_and_missing_resources_are_clean_4xx()
    {
        var (user, session, password) = await factory.CreateReadySessionAsync();
        var token = session.AccessToken;
        var admin = await factory.CreateAdminClientAsync();
        factory.Logs.Clear();

        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = user.Username, password = "Wrong#Passw0rd-1" });
        var change = await factory.CreateClientWithToken(token).PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "Wrong#Passw0rd-1", newPassword = "Another#Passw0rd-2026", confirmNewPassword = "Another#Passw0rd-2026" });
        var missing = await admin.GetAsync($"/api/v1/admin/users/{Guid.NewGuid()}");
        var forbidden = await factory.CreateClientWithToken(token).GetAsync("/api/v1/admin/users");
        var refresh = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = "garbage" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, change.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("invalid_credentials", (await ReadAsync(login)).GetProperty("code").GetString());
        Assert.Equal("invalid_current_password", (await ReadAsync(change)).GetProperty("code").GetString());
        Assert.Equal("not_found", (await ReadAsync(missing)).GetProperty("code").GetString());
        Assert.NotNull(password);

        AssertNoErrorsLogged();
    }

    [Fact]
    public async Task Malformed_json_is_a_400_not_a_500()
    {
        var admin = await factory.CreateAdminClientAsync();
        factory.Logs.Clear();

        var response = await admin.PostAsync("/api/v1/admin/users",
            new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoErrorsLogged();
    }
}
