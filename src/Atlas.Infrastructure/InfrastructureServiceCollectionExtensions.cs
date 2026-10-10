using Atlas.Application.Common.Abstractions;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Persistence.Repositories;
using Atlas.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Atlas.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "Atlas";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Strongly typed, validated JWT settings. ValidateOnStart => a missing/weak secret stops the host at startup.
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();

        // The connection string is resolved lazily and only from configuration providers
        // (environment variables / user-secrets / vault), never from committed files.
        services.AddDbContext<AtlasDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Connection string 'ConnectionStrings:Atlas' is not configured. " +
                    "Set the ConnectionStrings__Atlas environment variable or a user-secret.");

            options.UseNpgsql(connectionString);
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher>(_ => new BCryptPasswordHasher());
        services.AddSingleton<IPasswordGenerator, SecurePasswordGenerator>();
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserWriteScope, UserWriteScope>();
        services.AddScoped<IUserSessionValidator, UserSessionValidator>();

        return services;
    }
}
