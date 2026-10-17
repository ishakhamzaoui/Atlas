using System.Net.Http.Json;
using Atlas.Application.Departments;
using Atlas.Application.Organization;
using Atlas.Application.Profiles;

namespace Atlas.API.Tests.Support;

public static class OrganizationTestHelpers
{
    public static string UniqueName(string prefix) => prefix + " " + Guid.NewGuid().ToString("N")[..12];

    public static async Task<DepartmentDto> CreateDepartmentAsync(this AtlasApiFactory factory, string? name = null)
    {
        var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/admin/departments", new { name = name ?? UniqueName("Dept"), description = "test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DepartmentDto>())!;
    }

    public static async Task<ProfileDto> CreateProfileAsync(this AtlasApiFactory factory, string? name = null)
    {
        var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PostAsJsonAsync("/api/v1/admin/profiles", new { name = name ?? UniqueName("Profile"), description = "test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProfileDto>())!;
    }

    public static async Task<OrganizationChangeDto> AssignDepartmentAsync(this AtlasApiFactory factory, Guid userId, Guid departmentId, Guid? profileId = null)
    {
        var admin = await factory.CreateAdminClientAsync();
        var response = await admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/department", new { departmentId, profileId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrganizationChangeDto>())!;
    }
}
