using System.Net;
using System.Net.Http.Json;
using Atlas.API.Tests.Support;

namespace Atlas.API.Tests;

[Collection("api")]
public class HttpsRedirectionTests(AtlasApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/admin/users?page=1&pageSize=5")]
    public async Task Plain_http_get_is_redirected_to_https_on_the_default_port(string pathAndQuery)
    {
        var response = await factory.CreateInsecureClient().GetAsync(pathAndQuery);

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode); // 308
        // default HTTPS port (443) => no explicit port in the Location
        Assert.Equal($"https://localhost{pathAndQuery}", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Plain_http_post_is_redirected_not_processed()
    {
        var client = factory.CreateInsecureClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "admin", password = "x" });

        Assert.Equal(HttpStatusCode.PermanentRedirect, response.StatusCode);
        Assert.StartsWith("https://localhost/api/v1/auth/login", response.Headers.Location!.ToString());
        Assert.False(response.Headers.Contains("WWW-Authenticate")); // never reached authentication
    }

    [Fact]
    public async Task Https_requests_are_served_normally()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = "", password = "" });

        // 400 is the validation error from the endpoint itself, not a redirect.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
