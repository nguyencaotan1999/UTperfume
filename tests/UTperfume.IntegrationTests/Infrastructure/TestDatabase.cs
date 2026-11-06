using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>
/// Creates one uniquely named database per test run on a real SQL Server (LocalDB by default, never Docker)
/// and drops it on dispose. The server comes from the environment variable
/// <c>TestDatabase__ConnectionString</c>; the default is <c>(localdb)\MSSQLLocalDB</c> with Windows authentication.
/// </summary>
public sealed partial class TestDatabase : IAsyncLifetime
{
    public const string EnvironmentVariable = "TestDatabase__ConnectionString";

    public const string DefaultServerConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=60";

    private readonly string _serverConnectionString;

    public TestDatabase()
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        // Any Database/Initial Catalog in the supplied string is ignored: the server-level connection uses master,
        // and the per-run database name is always generated.
        _serverConnectionString = new SqlConnectionStringBuilder(
            string.IsNullOrWhiteSpace(configured) ? DefaultServerConnectionString : configured)
        {
            InitialCatalog = "master",
        }.ConnectionString;
        DatabaseName = $"UTperfume_Test_{Guid.NewGuid():N}";
        ConnectionString = new SqlConnectionStringBuilder(_serverConnectionString)
        {
            InitialCatalog = DatabaseName,
        }.ConnectionString;
    }

    public string DatabaseName { get; }

    /// <summary>Connection string of the per-run database.</summary>
    public string ConnectionString { get; }

    /// <summary>Catalogue tables in foreign-key delete order (children first). Concentration is seeded and kept.</summary>
    public static readonly string[] CatalogTablesInDeleteOrder =
    [
        "catalog.ProductNote",
        "catalog.ProductCategory",
        "catalog.ProductImage",
        "catalog.ProductVariant",
        "catalog.Product",
        "catalog.FragranceNote",
        "catalog.Category",
        "catalog.Brand",
        "catalog.HomeBanner",
    ];

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public Task ResetCatalogAsync() => ResetAsync(CatalogTablesInDeleteOrder);

    public async ValueTask InitializeAsync()
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{DatabaseName}]";
        await command.ExecuteNonQueryAsync();

        // MigrateAsync, never EnsureCreated, so the migrations themselves are tested.
        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{DatabaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; " +
            $"DROP DATABASE [{DatabaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> ExistsAsync()
    {
        await using var connection = new SqlConnection(_serverConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
        command.Parameters.AddWithValue("@name", DatabaseName);
        return (int)(await command.ExecuteScalarAsync())! == 1;
    }

    /// <summary>
    /// Deletes all rows from the given tables, in the given order. Pass child tables before parent
    /// tables (foreign-key order). Respawn is deliberately not used.
    /// </summary>
    public async Task ResetAsync(IEnumerable<string> tablesInDeleteOrder)
    {
        ArgumentNullException.ThrowIfNull(tablesInDeleteOrder);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        foreach (var table in tablesInDeleteOrder)
        {
            if (!TableName().IsMatch(table))
            {
                throw new ArgumentException($"Unsafe table name '{table}'.", nameof(tablesInDeleteOrder));
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM " + string.Join('.', table.Split('.').Select(p => $"[{p}]"));
            await command.ExecuteNonQueryAsync();
        }
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex TableName();
}
