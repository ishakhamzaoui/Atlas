using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Design-time only (used by <c>dotnet ef</c>). Reads <c>ConnectionStrings__Atlas</c>; falls back to a
/// credential-free placeholder so <c>migrations add</c> works without a database.
/// </summary>
public sealed class AtlasDbContextFactory : IDesignTimeDbContextFactory<AtlasDbContext>
{
    public AtlasDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Atlas")
                               ?? "Host=localhost;Port=5432;Database=atlas;Username=atlas;Password=pswd";

        var options = new DbContextOptionsBuilder<AtlasDbContext>().UseNpgsql(connectionString).Options;
        return new AtlasDbContext(options);
    }
}
