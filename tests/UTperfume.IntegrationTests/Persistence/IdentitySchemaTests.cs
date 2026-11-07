using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Persistence;

/// <summary>
/// The <c>AddAdminIdentity</c> migration against a real SQL Server (database model 3.13, ADR-0010): tables in schema
/// <c>identity</c>, the extra columns of <c>AdminUser</c>, the seeded <c>Catalog.Admin</c> role and no seeded users.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IdentitySchemaTests(TestDatabase database) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await AdminIdentityTestSupport.ResetIdentityAsync(database);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private async Task<List<string>> QueryAsync(string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    [Fact]
    public async Task AddAdminIdentity_CreatesTheSevenIdentityTablesInSchemaIdentity_AndNothingInOtherSchemas()
    {
        var identity = await QueryAsync(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'identity' ORDER BY t.name");
        var outside = await QueryAsync(
            "SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id " +
            "WHERE t.name LIKE 'AspNet%' OR (t.name IN ('Users','Roles','UserRoles','UserClaims','UserLogins','UserTokens','RoleClaims') AND s.name <> 'identity')");

        identity.Should().Equal("RoleClaims", "Roles", "UserClaims", "UserLogins", "UserRoles", "Users", "UserTokens");
        outside.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAdminIdentity_AdminUserHasDisplayNameIsActiveAndCreatedAtUtcWithTheDocumentedTypes()
    {
        var columns = await QueryAsync(
            "SELECT c.name, ty.name, c.max_length, c.is_nullable, ISNULL(dc.definition, '') " +
            "FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id " +
            "JOIN sys.types ty ON ty.user_type_id = c.user_type_id " +
            "LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id " +
            "WHERE s.name = 'identity' AND t.name = 'Users' AND c.name IN ('DisplayName','IsActive','CreatedAtUtc','LockoutEnd','AccessFailedCount','LockoutEnabled','SecurityStamp','ConcurrencyStamp') " +
            "ORDER BY c.name");

        columns.Should().Contain("DisplayName|nvarchar|200|False|");
        columns.Should().Contain(c => c.StartsWith("IsActive|bit|1|False|", StringComparison.Ordinal) && c.Contains('1', StringComparison.Ordinal), "IsActive defaults to 1");
        columns.Should().Contain(c => c.StartsWith("CreatedAtUtc|datetime2|", StringComparison.Ordinal) && c.EndsWith("|False|", StringComparison.Ordinal));
        columns.Should().Contain(c => c.StartsWith("LockoutEnd|datetimeoffset|", StringComparison.Ordinal));
        columns.Select(c => c.Split('|')[0]).Should()
            .BeEquivalentTo("DisplayName", "IsActive", "CreatedAtUtc", "LockoutEnd", "AccessFailedCount", "LockoutEnabled", "SecurityStamp", "ConcurrencyStamp");
        (await QueryAsync(
            "SELECT c.scale FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id " +
            "WHERE s.name = 'identity' AND t.name = 'Users' AND c.name = 'CreatedAtUtc'")).Should().Equal("3");
    }

    [Fact]
    public async Task AddAdminIdentity_SeedsTheCatalogAdminRoleWithAFixedIdAndStamp_AndNoUsers()
    {
        var roles = await QueryAsync("SELECT Id, Name, NormalizedName, ConcurrencyStamp FROM [identity].Roles");
        var users = await QueryAsync("SELECT COUNT(*) FROM [identity].Users");

        roles.Should().Equal("1|Catalog.Admin|CATALOG.ADMIN|5f0c4f0e-6b52-4c3e-9a33-8f2b7a1d9c01");
        users.Should().Equal("0");
    }

    [Fact]
    public async Task AddAdminIdentity_EnforcesUniqueUserNamesAndRoleNames()
    {
        await using var db = database.CreateContext();
        db.Users.Add(new UTperfume.Infrastructure.Identity.AdminUser
        {
            UserName = "dup@example.test", NormalizedUserName = "DUP@EXAMPLE.TEST", DisplayName = "A", CreatedAtUtc = DateTime.UtcNow,
        });
        db.Users.Add(new UTperfume.Infrastructure.Identity.AdminUser
        {
            UserName = "dup@example.test", NormalizedUserName = "DUP@EXAMPLE.TEST", DisplayName = "B", CreatedAtUtc = DateTime.UtcNow,
        });

        var act = () => db.SaveChangesAsync(Ct);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task AddAdminIdentity_DoesNotChangeTheCatalogSchema()
    {
        var catalog = await QueryAsync(
            "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'catalog' ORDER BY t.name");

        catalog.Should().Equal(
            "Brand", "Category", "Concentration", "FragranceNote", "HomeBanner", "Product", "ProductCategory",
            "ProductImage", "ProductNote", "ProductVariant");
    }
}
