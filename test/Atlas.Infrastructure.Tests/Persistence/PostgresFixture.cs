using Atlas.Infrastructure.Persistence;
using Atlas.Tests.Shared;

namespace Atlas.Infrastructure.Tests.Persistence;

/// <summary>
/// Uses the dedicated native test database from ATLAS_TEST_DB (an isolated, throw-away schema per fixture).
/// Never touches a real/dev database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private TestDatabase _database = default!;

    public string ConnectionString => _database.ConnectionString;

    public async Task InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        await _database.InitializeSchemaAsync();
    }

    public async Task DisposeAsync() => await _database.DisposeAsync();

    public AtlasDbContext CreateContext() => _database.CreateContext();
}
