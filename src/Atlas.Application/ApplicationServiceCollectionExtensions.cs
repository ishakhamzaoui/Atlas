using Atlas.Application.Bootstrap;
using Atlas.Application.Common.Behaviors;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <param name="mediatRLicenseKey">Optional MediatR license key (MediatR 13+); read from configuration by the host.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services, string? mediatRLicenseKey = null)
    {
        var assembly = typeof(ApplicationServiceCollectionExtensions).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            if (!string.IsNullOrWhiteSpace(mediatRLicenseKey))
                cfg.LicenseKey = mediatRLicenseKey;

            // Order matters: authorization runs before validation.
            cfg.AddOpenBehavior(typeof(AdminAuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddScoped<AdminBootstrapper>();
        return services;
    }
}
