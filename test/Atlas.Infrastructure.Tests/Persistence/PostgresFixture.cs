using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Atlas.Infrastructure.Tests.Persistence;

/// <summary>Throwaway PostgreSQL container (requires Docker). Never touches a real database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateContext();
        if (db.Database.GetMigrations().Any())
            await db.Database.MigrateAsync();      // exercises the real migrations once they exist
        else
            await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public AtlasDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AtlasDbContext>().UseNpgsql(ConnectionString).Options);
}
