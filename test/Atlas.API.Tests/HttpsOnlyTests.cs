using System.Net;
using System.Net.Http.Json;
using Atlas.API.Tests.Support;

namespace Atlas.API.Tests;

[Collection("api")]
public class HttpsOnlyTests(AtlasApiFactory factory)
{
    [Fact]
    public async Task Plain_http_requests_are_rejected_before_reaching_any_endpoint()
    {
        var client = factory.CreateInsecureClient();

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "x" });
        var me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, me.StatusCode);
        Assert.Contains("https_required", await login.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Https_requests_are_served()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = "", password = "" });

        // 400 here is the validation error from the endpoint itself, not the HTTPS guard.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("https_required", await response.Content.ReadAsStringAsync());
    }
}
