using Atlas.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Atlas.Tests.Shared;

/// <summary>
/// Where integration tests get their PostgreSQL database.
/// <list type="bullet">
/// <item><b>Native (preferred on the VM):</b> set <c>ATLAS_TEST_DB</c> to the connection string of a DEDICATED test database
/// (its name must contain "test"). Each fixture creates its own throw-away schema inside it, runs the real migrations there and
/// drops the schema afterwards, so fixtures and test projects can run in parallel and no data is ever shared or reused.</item>
/// <item><b>Container fallback:</b> when the variable is not set, a disposable PostgreSQL container is started (needs Docker).</item>
/// </list>
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    public const string ConnectionStringVariable = "ATLAS_TEST_DB";

    private readonly PostgreSqlContainer? _container;
    private readonly string? _adminConnectionString;
    private readonly string? _schema;

    private TestDatabase(string connectionString, PostgreSqlContainer? container, string? adminConnectionString, string? schema)
    {
        ConnectionString = connectionString;
        _container = container;
        _adminConnectionString = adminConnectionString;
        _schema = schema;
    }

    /// <summary>Connection string to use for the application under test (includes the private schema in native mode).</summary>
    public string ConnectionString { get; }

    public bool IsNative => _schema is not null;

    public static async Task<TestDatabase> CreateAsync()
    {
        var configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        return string.IsNullOrWhiteSpace(configured)
            ? await CreateContainerAsync()
            : await CreateNativeAsync(configured);
    }

    private static async Task<TestDatabase> CreateContainerAsync()
    {
        var container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
        await container.StartAsync();
        return new TestDatabase(container.GetConnectionString(), container, null, null);
    }

    private static async Task<TestDatabase> CreateNativeAsync(string configured)
    {
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
        return new TestDatabase(scoped.ConnectionString, null, builder.ConnectionString, schema);
    }

    /// <summary>
    /// Creates the schema from the real migrations (or, before any migration exists, from the current model).
    /// Executed with plain Npgsql inside this database's own search path so concurrent fixtures cannot interfere.
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
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_schema is not null && _adminConnectionString is not null)
        {
            await using (var scoped = new NpgsqlConnection(ConnectionString))
                NpgsqlConnection.ClearPool(scoped);

            await using var connection = new NpgsqlConnection(_adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{_schema}\" CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
