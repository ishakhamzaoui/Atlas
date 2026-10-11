using Atlas.API.Bootstrap;
using Atlas.API.Extensions;
using Atlas.Application;
using Atlas.Infrastructure;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Leveled logging (Verbose/Debug/Information/Warning/Error/Fatal) driven entirely by configuration:
// appsettings.json = Information, appsettings.Development.json = Debug. Override at runtime with e.g. Serilog__MinimumLevel__Default=Warning.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddApplication(builder.Configuration["MediatR:LicenseKey"]);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

// One-time, explicit first-run setup:  dotnet run --project src/Atlas.API -- --bootstrap-admin
if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    var exitCode = await AdminBootstrapRunner.RunAsync(app.Services, app.Configuration);
    await app.DisposeAsync(); // flushes log sinks
    return exitCode;
}

// HTTPS only: refuse to start if any plain-HTTP address is configured (ASPNETCORE_URLS, launch profile, Urls setting).
// (Checked against both the server's address list and the raw "urls" setting, which also carries ASPNETCORE_URLS / --urls.)
var configuredUrls = (app.Configuration["urls"] ?? string.Empty)
    .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var insecureUrls = app.Urls.Concat(configuredUrls)
    .Where(u => u.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
    .Distinct()
    .ToList();
if (insecureUrls.Count > 0)
{
    throw new InvalidOperationException(
        $"Plain HTTP endpoints are not allowed ({string.Join(", ", insecureUrls)}). Configure HTTPS URLs only, " +
        "e.g. ASPNETCORE_URLS=https://0.0.0.0:7000, and a Kestrel certificate.");
}

app.UseExceptionHandler();

// Defense in depth: reject any request that did not arrive over TLS (e.g. through a misconfigured proxy).
if (app.Configuration.GetValue("Server:RequireHttps", true))
{
    app.Use(async (context, next) =>
    {
        if (!context.Request.IsHttps)
        {
            app.Logger.LogWarning("Rejected non-HTTPS request {Method} {Path} from {RemoteIp}",
                context.Request.Method, context.Request.Path, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = "HTTPS is required.", code = "https_required" });
            return;
        }

        await next();
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // browsers will only talk HTTPS to this host from now on
}

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500 ? LogEventLevel.Error
        : httpContext.Response.StatusCode >= 400 ? LogEventLevel.Warning
        : httpContext.Request.Path.StartsWithSegments("/swagger") ? LogEventLevel.Debug
        : LogEventLevel.Information;

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RemoteIp", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("UserId", httpContext.User.FindFirst("sub")?.Value);
    };
});

// Never let browsers/proxies cache API responses (they can contain tokens or one-time temporary passwords).
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Task.CompletedTask;
        });
    }

    await next();
});

// Swagger UI is a development tool only; it is not mapped in other environments.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Atlas API v1"));
}

app.UseRateLimiter();
app.UseAuthentication(); // must run before authorization
app.UseAuthorization();

app.MapControllers();

app.Run();
return 0;

public partial class Program { }
