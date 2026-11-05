using Microsoft.Data.SqlClient;

namespace UTperfume.IntegrationTests.Infrastructure;

[Collection(DatabaseCollection.Name)]
public sealed class TestDatabaseTests(TestDatabase database)
{
    private static async Task<int> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Database_is_uniquely_named_and_reachable()
    {
        database.DatabaseName.Should().StartWith("UTperfume_Test_");
        (await database.ExistsAsync()).Should().BeTrue();
        (await ScalarAsync(database.ConnectionString, "SELECT 1")).Should().Be(1);
    }

    [Fact]
    public async Task Reset_deletes_in_the_given_foreign_key_order()
    {
        await ExecAsync(database.ConnectionString, """
            IF OBJECT_ID('dbo.ResetChild') IS NOT NULL DROP TABLE dbo.ResetChild;
            IF OBJECT_ID('dbo.ResetParent') IS NOT NULL DROP TABLE dbo.ResetParent;
            CREATE TABLE dbo.ResetParent (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.ResetChild (Id int NOT NULL PRIMARY KEY, ParentId int NOT NULL REFERENCES dbo.ResetParent(Id));
            INSERT dbo.ResetParent VALUES (1);
            INSERT dbo.ResetChild VALUES (1, 1);
            """);

        await database.ResetAsync(["dbo.ResetChild", "dbo.ResetParent"]);

        (await ScalarAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.ResetChild")).Should().Be(0);
        (await ScalarAsync(database.ConnectionString, "SELECT COUNT(*) FROM dbo.ResetParent")).Should().Be(0);
    }

    [Fact]
    public async Task Reset_with_parent_first_violates_the_foreign_key()
    {
        await ExecAsync(database.ConnectionString, """
            IF OBJECT_ID('dbo.ResetChild') IS NOT NULL DROP TABLE dbo.ResetChild;
            IF OBJECT_ID('dbo.ResetParent') IS NOT NULL DROP TABLE dbo.ResetParent;
            CREATE TABLE dbo.ResetParent (Id int NOT NULL PRIMARY KEY);
            CREATE TABLE dbo.ResetChild (Id int NOT NULL PRIMARY KEY, ParentId int NOT NULL REFERENCES dbo.ResetParent(Id));
            INSERT dbo.ResetParent VALUES (1);
            INSERT dbo.ResetChild VALUES (1, 1);
            """);

        var act = () => database.ResetAsync(["dbo.ResetParent", "dbo.ResetChild"]);

        await act.Should().ThrowAsync<SqlException>();
        await database.ResetAsync(["dbo.ResetChild", "dbo.ResetParent"]);
    }

    [Theory]
    [InlineData("dbo.Users; DROP TABLE x")]
    [InlineData("a.b.c")]
    [InlineData("")]
    [InlineData("[dbo].[x]")]
    public async Task Reset_rejects_unsafe_table_names(string table)
    {
        var act = () => database.ResetAsync([table]);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // Architecture 10.2 step 4: the engine behind the test database supports what the schema in 0002 needs.
    [Fact]
    public async Task Engine_supports_schemas_filtered_unique_indexes_rowversion_and_check_constraints()
    {
        await ExecAsync(database.ConnectionString, """
            IF OBJECT_ID('probe.Item') IS NOT NULL DROP TABLE probe.Item;
            IF SCHEMA_ID('probe') IS NULL EXEC('CREATE SCHEMA probe');
            CREATE TABLE probe.Item (
                Id int IDENTITY PRIMARY KEY,
                OwnerId int NOT NULL,
                IsPrimary bit NOT NULL,
                Price bigint NOT NULL CONSTRAINT CK_Item_Price CHECK (Price >= 0),
                RowVersion rowversion NOT NULL);
            CREATE UNIQUE INDEX UX_Item_Primary ON probe.Item (OwnerId) WHERE IsPrimary = 1;
            INSERT probe.Item (OwnerId, IsPrimary, Price) VALUES (1, 1, 100), (1, 0, 0);
            """);

        var duplicatePrimary = () => ExecAsync(database.ConnectionString,
            "INSERT probe.Item (OwnerId, IsPrimary, Price) VALUES (1, 1, 5)");
        var negativePrice = () => ExecAsync(database.ConnectionString,
            "INSERT probe.Item (OwnerId, IsPrimary, Price) VALUES (2, 0, -1)");

        await duplicatePrimary.Should().ThrowAsync<SqlException>().WithMessage("*UX_Item_Primary*");
        await negativePrice.Should().ThrowAsync<SqlException>().WithMessage("*CK_Item_Price*");
        (await ScalarAsync(database.ConnectionString, "SELECT COUNT(*) FROM probe.Item WHERE RowVersion IS NOT NULL"))
            .Should().Be(2);
    }

    [Fact]
    public async Task A_separate_instance_creates_and_then_drops_its_database()
    {
        var other = new TestDatabase();
        await other.InitializeAsync();
        (await other.ExistsAsync()).Should().BeTrue();

        await other.DisposeAsync();

        (await other.ExistsAsync()).Should().BeFalse();
    }
}
