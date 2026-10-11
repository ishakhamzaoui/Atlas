using System.Text;
using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Atlas.API.Security;

/// <summary>Builds the JWT bearer configuration from the validated <see cref="JwtOptions"/>.</summary>
internal sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;

        var jwt = jwtOptions.Value; // throws OptionsValidationException if secrets are missing/weak

        options.MapInboundClaims = false; // keep claim names exactly as issued ("sub", "role", ...)
        options.SaveToken = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = AuthClaims.UniqueName,
            RoleClaimType = AuthClaims.Role
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = ValidateSessionAsync,
            OnAuthenticationFailed = context =>
            {
                // Reason only (exception type); never the token. DEBUG because probes/garbage tokens are routine.
                Logger(context.HttpContext).LogDebug("JWT rejected: {Reason}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            },
            OnForbidden = context =>
            {
                Logger(context.HttpContext).LogWarning("Access forbidden for user {UserId} on {Method} {Path}",
                    context.Principal?.FindFirst(AuthClaims.Subject)?.Value, context.Request.Method, context.Request.Path);
                return Task.CompletedTask;
            }
        };
    }

    /// <summary>
    /// Rejects otherwise-valid tokens whose user was deactivated/deleted or whose security stamp rotated
    /// (password change/reset, role change, rename, deactivation).
    /// </summary>
    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var hasUser = Guid.TryParse(principal?.FindFirst(AuthClaims.Subject)?.Value, out var userId);
        var hasStamp = Guid.TryParse(principal?.FindFirst(AuthClaims.SecurityStamp)?.Value, out var stamp);

        if (!hasUser || !hasStamp)
        {
            Logger(context.HttpContext).LogWarning("Validly signed token without required claims was rejected");
            context.Fail("Token is missing required claims.");
            return;
        }

        var validator = context.HttpContext.RequestServices.GetRequiredService<IUserSessionValidator>();
        if (!await validator.IsSessionValidAsync(userId, stamp, context.HttpContext.RequestAborted))
        {
            Logger(context.HttpContext).LogInformation("Token for user {UserId} rejected: session no longer valid", userId);
            context.Fail("Session is no longer valid.");
        }
    }

    private static ILogger Logger(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Atlas.API.Security.Jwt");
}
