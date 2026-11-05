using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using UTperfume.Application.Common;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.Infrastructure;

public static class DependencyInjection
{
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

        return services;
    }
}
