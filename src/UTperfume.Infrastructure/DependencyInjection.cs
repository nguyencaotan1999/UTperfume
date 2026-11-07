using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Infrastructure.Persistence;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.Infrastructure;

public static class DependencyInjection
{
    public const string CatalogConnectionName = "Catalog";

    /// <summary>Composition root for infrastructure services. Must not open any connection while registering.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<ImageStorageOptions>, ImageStorageOptionsValidator>();
        services.AddOptions<ImageStorageOptions>()
            .Bind(configuration.GetSection(ImageStorageOptions.SectionName));

        services.TryAddSingleton<AzureBlobImageStorage>();
        services.TryAddSingleton<IImageStorage>(static sp => sp.GetRequiredService<AzureBlobImageStorage>());

        // The connection string is read when a context is first created, not here, so the build-time OpenAPI
        // generator and tests that never touch the database need no connection (ADR-0009).
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connection = sp.GetRequiredService<IConfiguration>().GetConnectionString(CatalogConnectionName);
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException($"Connection string 'ConnectionStrings:{CatalogConnectionName}' is not configured.");
            }

            options.UseSqlServer(connection, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name));
        });
        services.AddScoped<ICatalogDbContext>(static sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAdminWriteSession, AdminWriteSession>();
        services.AddScoped<IProductWriteLock, ProductWriteLock>();

        return services;
    }

    /// <summary>Registers the development seeder. Call it only in the Development environment.</summary>
    public static IServiceCollection AddDevelopmentSeeding(this IServiceCollection services, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);

        services.AddScoped(sp => new DevelopmentSeeder(
            sp.GetRequiredService<AppDbContext>(),
            sp.GetRequiredService<IImageStorage>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DevelopmentSeeder>>(),
            DevelopmentSeeder.FindSeedImagesDirectory(contentRootPath)));
        return services;
    }
}
