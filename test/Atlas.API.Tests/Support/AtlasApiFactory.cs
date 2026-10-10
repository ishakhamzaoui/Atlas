using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Atlas.Application.Auth;
using Atlas.Application.Bootstrap;
using Atlas.Application.Users;
using Atlas.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Atlas.API.Tests.Support;

/// <summary>Boots the real API against a throwaway PostgreSQL container (requires Docker).</summary>
public sealed class AtlasApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "atlas-tests";
    public const string Audience = "atlas-tests-clients";
    public const string BootstrapPassword = "Bootstrap#Temp-2026-xK9";
    public const string AdminPermanentPassword = "Permanent#Admin-2026-qZ7";
    public const string InitialUserPassword = "Initial#Passw0rd-2026";

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private readonly Lazy<Task<AdminSession>> _adminSession;

    public AtlasApiFactory()
    {
        _adminSession = new Lazy<Task<AdminSession>>(EstablishAdminSessionAsync);
    }

    /// <summary>Random per test run; never a real secret.</summary>
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public sealed record AdminSession(LoginResponse FirstLogin, string Token);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Atlas", _db.GetConnectionString());
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AtlasDbContext>();
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();

        // Real bootstrap path: creates 'admin' with a temporary password and MustChangePassword = true.
        var result = await scope.ServiceProvider.GetRequiredService<AdminBootstrapper>().RunAsync(BootstrapPassword, default);
        if (result.Status != BootstrapStatus.Created)
            throw new InvalidOperationException($"Bootstrap failed: {result.Status}");
    }

    async Task IAsyncLifetime.DisposeAsync() => await _db.DisposeAsync();

    // ---------- helpers ----------

    public Task<AdminSession> GetAdminSessionAsync() => _adminSession.Value;

    /// <summary>First login with the bootstrap password, then the forced password change.</summary>
    private async Task<AdminSession> EstablishAdminSessionAsync()
    {
        var client = CreateClient();
        var first = await LoginAsync(client, "admin", BootstrapPassword);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/change-password")
        {
            Content = JsonContent.Create(new
            {
                currentPassword = BootstrapPassword,
                newPassword = AdminPermanentPassword,
                confirmNewPassword = AdminPermanentPassword
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var changed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        return new AdminSession(first, changed.AccessToken);
    }

    public async Task<HttpClient> CreateAdminClientAsync()
    {
        var session = await GetAdminSessionAsync();
        return CreateClientWithToken(session.Token);
    }

    public HttpClient CreateClientWithToken(string? token)
    {
        var client = CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    public static async Task<LoginResponse> LoginAsync(HttpClient client, string username, string password)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    public static string UniqueUsername(string prefix = "u") => prefix + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Creates a user as the admin and returns it with its temporary password.</summary>
    public async Task<(UserDto User, string Password)> CreateUserAsync(string role = "User", string? username = null)
    {
        var admin = await CreateAdminClientAsync();
        var name = username ?? UniqueUsername();
        var response = await admin.PostAsJsonAsync("/api/v1/admin/users",
            new { username = name, password = InitialUserPassword, role });
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<UserDto>())!, InitialUserPassword);
    }

    /// <summary>Creates a user and completes the forced password change, returning an unrestricted token.</summary>
    public async Task<(UserDto User, string Token, string Password)> CreateReadyUserAsync(string role = "User")
    {
        const string permanent = "Permanent#User-2026-mN4";
        var (user, temp) = await CreateUserAsync(role);
        var client = CreateClient();
        var first = await LoginAsync(client, user.Username, temp);

        var authed = CreateClientWithToken(first.AccessToken);
        var response = await authed.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = temp, newPassword = permanent, confirmNewPassword = permanent });
        response.EnsureSuccessStatusCode();
        var changed = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        return (user, changed.AccessToken, permanent);
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<AtlasApiFactory>;
