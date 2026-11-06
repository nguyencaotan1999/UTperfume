using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UTperfume.Infrastructure.Persistence;

/// <summary>
/// Design-time only (<c>dotnet ef migrations add</c> and <c>migrations script</c>). Those commands do not open a
/// connection. For <c>database update</c> always pass <c>--connection</c> explicitly; the placeholder below is a
/// throwaway name and holds no credentials.
/// </summary>
internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string PlaceholderConnection =
        @"Server=(localdb)\MSSQLLocalDB;Database=UTperfume_Design;Integrated Security=true;TrustServerCertificate=true";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Catalog");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(string.IsNullOrWhiteSpace(connection) ? PlaceholderConnection : connection)
            .Options;
        return new AppDbContext(options);
    }
}
