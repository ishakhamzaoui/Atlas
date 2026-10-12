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

// Outermost on purpose: it must see the FINAL status code, after the exception handler below has turned any
// exception into a response. (Placed inside the handler it would log every handled exception as an HTTP 500 error.)
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

app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // browsers that reached the site over HTTPS will only use HTTPS from now on
}

// Plain HTTP (port 80) only exists to redirect to HTTPS (port 443): 308 keeps the method and body of the retried request.
app.UseHttpsRedirection();

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
