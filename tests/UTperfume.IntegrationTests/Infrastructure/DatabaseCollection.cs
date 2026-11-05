namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>
/// All database-backed tests share one database and run sequentially in this single collection (ADR-0013).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseCollection : ICollectionFixture<TestDatabase>
{
    public const string Name = "Database";
}
