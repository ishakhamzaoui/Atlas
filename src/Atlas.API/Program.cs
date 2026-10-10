using Atlas.API.Bootstrap;
using Atlas.API.Extensions;
using Atlas.Application;
using Atlas.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication(builder.Configuration["MediatR:LicenseKey"]);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApiServices(builder.Configuration);

var app = builder.Build();

// One-time, explicit first-run setup:  dotnet run --project src/Atlas.API -- --bootstrap-admin
if (args.Contains("--bootstrap-admin", StringComparer.Ordinal))
{
    return await AdminBootstrapRunner.RunAsync(app.Services, app.Configuration);
}

app.UseExceptionHandler();

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

app.UseHttpsRedirection();

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
