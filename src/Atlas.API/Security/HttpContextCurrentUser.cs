using System.Security.Claims;
using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Domain.Users;

namespace Atlas.API.Security;

/// <summary>Identity comes exclusively from the validated JWT principal, never from request bodies or headers.</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirst(AuthClaims.Subject)?.Value, out var id) ? id : null;

    public string? Username => Principal?.FindFirst(AuthClaims.UniqueName)?.Value;

    public bool IsAdmin => Principal?.IsInRole(nameof(UserRole.Admin)) == true;
}
