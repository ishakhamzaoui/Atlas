using System.Net;
using Atlas.API.Tests.Support;

namespace Atlas.API.Tests;

[Collection("api")]
public class JwtValidationTests(AtlasApiFactory factory)
{
    private const string Protected = "/api/v1/auth/me";

    private async Task<HttpStatusCode> GetAsync(string? token) =>
        (await factory.CreateClientWithToken(token).GetAsync(Protected)).StatusCode;

    [Fact]
    public async Task Anonymous_request_to_protected_endpoint_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(null));

    [Fact]
    public async Task Malformed_token_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync("not-a-jwt"));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync("a.b.c"));
    }

    [Fact]
    public async Task Valid_token_is_accepted_and_tampering_is_rejected()
    {
        var (user, token, _) = await factory.CreateReadyUserAsync();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(token));

        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{new string(parts[2].Reverse().ToArray())}";
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(tampered));
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Token_signed_with_wrong_key_is_401()
    {
        var (user, _, _) = await factory.CreateReadyUserAsync();
        var forged = TestTokens.Create("some-other-signing-key-which-is-long-enough-123456", user.Id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(forged));
    }

    [Fact]
    public async Task Expired_token_is_401()
    {
        var (user, _, _) = await factory.CreateReadyUserAsync();
        var expired = TestTokens.Create(factory.SigningKey, user.Id, Guid.NewGuid(),
            notBefore: DateTime.UtcNow.AddHours(-2), expires: DateTime.UtcNow.AddHours(-1));
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(expired));
    }

    [Fact]
    public async Task Wrong_issuer_or_audience_is_401()
    {
        var (user, _, _) = await factory.CreateReadyUserAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,
            await GetAsync(TestTokens.Create(factory.SigningKey, user.Id, Guid.NewGuid(), issuer: "evil")));
        Assert.Equal(HttpStatusCode.Unauthorized,
            await GetAsync(TestTokens.Create(factory.SigningKey, user.Id, Guid.NewGuid(), audience: "evil")));
    }

    [Fact]
    public async Task Correctly_signed_token_with_stale_security_stamp_is_401()
    {
        var (user, _, _) = await factory.CreateReadyUserAsync();
        var stale = TestTokens.Create(factory.SigningKey, user.Id, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(stale));
    }

    [Fact]
    public async Task Correctly_signed_token_for_unknown_user_is_401() =>
        Assert.Equal(HttpStatusCode.Unauthorized,
            await GetAsync(TestTokens.Create(factory.SigningKey, Guid.NewGuid(), Guid.NewGuid(), role: "Admin")));

    [Fact]
    public async Task Role_claim_in_a_token_cannot_be_forged_without_the_signing_key()
    {
        var (user, _, _) = await factory.CreateReadyUserAsync();
        var forgedAdmin = TestTokens.Create("attacker-key-attacker-key-attacker-key-123", user.Id, Guid.NewGuid(), role: "Admin");
        var client = factory.CreateClientWithToken(forgedAdmin);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
    }

    [Fact]
    public async Task Deactivating_a_user_invalidates_their_existing_token()
    {
        var (user, token, _) = await factory.CreateReadyUserAsync();
        Assert.Equal(HttpStatusCode.OK, await GetAsync(token));

        var admin = await factory.CreateAdminClientAsync();
        (await admin.PatchAsync($"/api/v1/admin/users/{user.Id}/status",
            System.Net.Http.Json.JsonContent.Create(new { isActive = false }))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, await GetAsync(token));
    }
}
