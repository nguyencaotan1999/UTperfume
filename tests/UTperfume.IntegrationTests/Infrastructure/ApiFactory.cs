using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UTperfume.Application.Common;

namespace UTperfume.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in-process against the per-run SQL Server database, with the fake image storage
/// and the test authentication scheme. The environment is <c>Testing</c>, so Development-only behaviour
/// (Azurite container creation, exception details) is off.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly TestDatabase _database;
    private readonly IReadOnlyDictionary<string, string?> _overrides;
    private readonly string _environment;
    private readonly string _webRoot;
    private readonly bool _useProjectWebRoot;

    public ApiFactory(
        TestDatabase database,
        IReadOnlyDictionary<string, string?>? overrides = null,
        string environment = "Testing",
        bool useProjectWebRoot = false)
    {
        _database = database;
        _overrides = overrides ?? new Dictionary<string, string?>();
        _environment = environment;
        _useProjectWebRoot = useProjectWebRoot;
        _webRoot = Path.Combine(Path.GetTempPath(), "utperfume-webroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_webRoot);
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<!doctype html><html><body>spa-index</body></html>");
    }

    public InMemoryImageStorage ImageStorage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        if (!_useProjectWebRoot)
        {
            builder.UseWebRoot(_webRoot);
        }

        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Catalog"] = _database.ConnectionString,
            ["Storage:Images:ConnectionString"] = "UseDevelopmentStorage=true",
            ["Storage:Images:Container"] = "catalog-images",
            ["Storage:Images:PublicBaseUrl"] = InMemoryImageStorage.PublicBaseUrl,
            // Tests seed their own data; the Development seeder is exercised explicitly in DevelopmentSeedTests.
            ["DevelopmentSeed:Enabled"] = "false",
        };
        foreach (var (key, value) in _overrides)
        {
            settings[key] = value;
        }

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IImageStorage>();
            services.AddSingleton<IImageStorage>(ImageStorage);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, null);
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }
}
