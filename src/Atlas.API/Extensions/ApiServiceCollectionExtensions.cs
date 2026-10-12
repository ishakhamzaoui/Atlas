using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Atlas.API.Errors;
using Atlas.API.Security;
using Atlas.Application.Common;
using Atlas.Application.Common.Abstractions;
using Atlas.Domain.Users;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Atlas.API.Extensions;

public sealed class ApiRateLimitOptions
{
    public const string SectionName = "RateLimiting";
    public int AuthPermitLimit { get; set; } = 10;
    public int AuthWindowSeconds { get; set; } = 60;
}

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.AddControllers(o => o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // HTTP -> HTTPS redirection on the default ports (80 -> 443). 308 preserves the HTTP method and body.
        services.AddHttpsRedirection(options =>
        {
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            options.HttpsPort = configuration.GetValue("Server:HttpsPort", 443);
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        AddAuthenticationAndAuthorization(services);
        AddRateLimiting(services, configuration);
        AddSwagger(services);

        return services;
    }

    private static void AddAuthenticationAndAuthorization(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.ConfigureOptions<ConfigureJwtBearerOptions>();

        static bool NoPendingPasswordChange(AuthorizationHandlerContext ctx) =>
            !ctx.User.HasClaim(AuthClaims.MustChangePassword, "true");

        var fullAccess = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(NoPendingPasswordChange)
            .Build();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(fullAccess)
            .SetFallbackPolicy(fullAccess) // anything not explicitly opened up requires a fully-active session
            .AddPolicy(Policies.FullAccess, fullAccess)
            .AddPolicy(Policies.Authenticated, p => p.RequireAuthenticatedUser())
            .AddPolicy(Policies.AdminOnly, p => p
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Admin))
                .RequireAssertion(NoPendingPasswordChange));
    }

    private static void AddRateLimiting(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiRateLimitOptions>()
            .Bind(configuration.GetSection(ApiRateLimitOptions.SectionName))
            .Validate(o => o.AuthPermitLimit > 0 && o.AuthWindowSeconds > 0, "RateLimiting values must be positive.")
            .ValidateOnStart();

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<ApiRateLimitOptions>>((limiter, settings) =>
        {
            var s = settings.Value;
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.AddPolicy(RateLimitPolicies.Auth, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = s.AuthPermitLimit,
                        Window = TimeSpan.FromSeconds(s.AuthWindowSeconds),
                        QueueLimit = 0
                    }));
        });
    }

    private static void AddSwagger(IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Atlas API",
                Version = "v1",
                Description = "Authentication and user management. Log in via POST /api/v1/auth/login, " +
                              "then use the Authorize button and paste the returned accessToken."
            });

            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste only the JWT access token (Swagger adds the 'Bearer ' prefix)."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference("Bearer", document)] = []
            });

            var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
            if (File.Exists(xmlFile)) options.IncludeXmlComments(xmlFile);
        });
    }
}
