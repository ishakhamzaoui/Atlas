using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace Atlas.Tests.Shared;

/// <summary>
/// The PostgreSQL database used by integration tests: a DEDICATED, natively installed test database whose connection string is
/// supplied through the <c>ATLAS_TEST_DB</c> environment variable (its name must contain "test").
/// Each fixture creates its own throw-away schema inside it, builds it from the real EF migrations, and drops it afterwards,
/// so fixtures and test projects can run in parallel and no data is ever shared or reused between them.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public const string ConnectionStringVariable = "ATLAS_TEST_DB";

    private readonly string _adminConnectionString;
    private readonly string _schema;

    private TestDatabase(string connectionString, string adminConnectionString, string schema)
    {
        ConnectionString = connectionString;
        _adminConnectionString = adminConnectionString;
        _schema = schema;
    }

    /// <summary>Connection string for the application under test (it includes this fixture's private schema).</summary>
    public string ConnectionString { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} is not set. Integration tests run against a dedicated native PostgreSQL test database. " +
                "Create it with ./scripts/create-test-db.sh and export the connection string it prints, e.g. " +
                $"{ConnectionStringVariable}='Host=localhost;Port=5432;Database=atlas_test;Username=atlas_test;Password=<pw>'.");
        }

        var builder = new NpgsqlConnectionStringBuilder(configured);

        // Safety net: these fixtures create and drop schemas, so refuse anything that does not look like a test database.
        if (string.IsNullOrWhiteSpace(builder.Database) ||
            !builder.Database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringVariable} must point at a dedicated test database whose name contains 'test' " +
                $"(got '{builder.Database}'). Refusing to run tests against it.");
        }

        var schema = "t_" + Guid.NewGuid().ToString("N")[..16];

        await using (var connection = new NpgsqlConnection(builder.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        var scoped = new NpgsqlConnectionStringBuilder(builder.ConnectionString) { SearchPath = schema };
        return new TestDatabase(scoped.ConnectionString, builder.ConnectionString, schema);
    }

    /// <summary>
    /// Creates the schema from the real migrations (or, before any migration exists, from the current model).
    /// Executed with plain Npgsql inside this fixture's own search path so concurrent fixtures cannot interfere.
    /// </summary>
    public async Task InitializeSchemaAsync()
    {
        await using var db = CreateContext();

        var script = db.Database.GetMigrations().Any()
            ? db.GetService<IMigrator>().GenerateScript()
            : db.Database.GenerateCreateScript();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    public AtlasDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AtlasDbContext>().UseNpgsql(ConnectionString).Options);

    public async ValueTask DisposeAsync()
    {
        await using (var scoped = new NpgsqlConnection(ConnectionString))
            NpgsqlConnection.ClearPool(scoped);

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }
}
