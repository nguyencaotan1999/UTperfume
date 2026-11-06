using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UTperfume.Api.Infrastructure;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;
using UTperfume.Application.Features.Products;
using UTperfume.IntegrationTests.Infrastructure;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class DevelopmentSeedTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync() => await database.ResetCatalogAsync();

    public async ValueTask DisposeAsync() => await database.ResetCatalogAsync();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class UnreachableStorage : IImageStorage
    {
        public int Attempts { get; private set; }

        public Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
        {
            Attempts++;
            throw new HttpRequestException("storage is down");
        }

        public Task DeleteAsync(string blobPath, CancellationToken cancellationToken) => Task.CompletedTask;

        public string GetPublicUrl(string blobPath) => blobPath;
    }

    private static string? ImagesDirectory() => DevelopmentSeeder.FindSeedImagesDirectory(AppContext.BaseDirectory);

    private DevelopmentSeeder NewSeeder(AppDbContext db, IImageStorage storage, string? imagesDirectory) =>
        new(db, storage, new FixedTimeProvider(Now), NullLogger<DevelopmentSeeder>.Instance, imagesDirectory);

    [Fact]
    public async Task Seed_images_directory_is_found_from_the_test_output()
    {
        ImagesDirectory().Should().NotBeNull();
        Directory.EnumerateFiles(ImagesDirectory()!, "*.png").Should().HaveCountGreaterThanOrEqualTo(3);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Seeds_about_forty_products_with_vnd_prices_and_all_three_availability_statuses()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryImageStorage();
        await using var db = database.CreateContext();

        var result = await NewSeeder(db, storage, ImagesDirectory()).SeedAsync(ct);

        result.Seeded.Should().BeTrue();
        result.Products.Should().BeInRange(40, 50);

        await using var read = database.CreateContext();
        var variants = await read.ProductVariants.AsNoTracking().Where(v => v.IsActive).ToListAsync(ct);
        variants.Should().OnlyContain(v => v.Price >= 1_250_000 && v.Price <= 8_900_000,
            "seed prices are whole VND between 1,250,000 and 8,900,000");
        variants.Should().OnlyContain(v => v.Price % 10_000 == 0);
        variants.Where(v => v.OriginalPrice != null).Should().OnlyContain(v => v.OriginalPrice > v.Price)
            .And.NotBeEmpty();

        var statuses = (await read.ProductVariants.ToAvailabilityRows(5).ToListAsync(ct))
            .Select(r => r.Availability).Distinct().ToList();
        statuses.Should().HaveCount(3).And.Contain(
            [AvailabilityStatus.OutOfStock, AvailabilityStatus.LowStock, AvailabilityStatus.InStock]);
    }

    [Fact]
    public async Task Seeded_data_satisfies_the_domain_rules_and_covers_the_visibility_edge_cases()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = database.CreateContext();
        await NewSeeder(db, new InMemoryImageStorage(), ImagesDirectory()).SeedAsync(ct);

        await using var read = database.CreateContext();
        (await read.Products.AsNoTracking().Select(p => p.Slug).ToListAsync(ct)).Should().OnlyContain(s => Slug.IsValid(s));
        (await read.Brands.AsNoTracking().Select(b => b.Slug).ToListAsync(ct)).Should().OnlyContain(s => Slug.IsValid(s));
        (await read.ProductVariants.AsNoTracking().Select(v => v.Sku).ToListAsync(ct))
            .Should().OnlyContain(s => VariantRules.IsValidSku(s));

        var statuses = await read.Products.AsNoTracking().Select(p => p.Status).Distinct().ToListAsync(ct);
        statuses.Should().HaveCount(3).And.Contain([ProductStatus.Published, ProductStatus.Draft, ProductStatus.Archived]);

        var total = await read.Products.CountAsync(ct);
        var visible = await read.Products.VisibleProducts().CountAsync(ct);
        visible.Should().BeLessThan(total, "draft, archived, inactive-brand and no-active-variant products are hidden");
        (total - visible).Should().Be(4);

        (await read.Products.AsNoTracking().AnyAsync(p => p.Slug.Contains("dem-sai-gon"), ct))
            .Should().BeTrue("a product name with 'đ' must slugify to 'd'");
        (await read.Products.AsNoTracking().Where(p => p.Images.Count == 0).CountAsync(ct)).Should().BeGreaterThan(0);

        // One primary image per product that has images (also enforced by the filtered unique index).
        var imageGroups = await read.ProductImages.AsNoTracking().GroupBy(i => i.ProductId)
            .Select(g => new { Primaries = g.Count(i => i.IsPrimary) }).ToListAsync(ct);
        imageGroups.Should().OnlyContain(g => g.Primaries == 1);

        (await read.HomeBanners.CountAsync(ct)).Should().BeGreaterThan(0);
        (await read.HomeBanners.AsNoTracking().Select(b => b.LinkUrl).ToListAsync(ct)).Should().OnlyContain(l => l!.StartsWith('/'));
    }

    [Fact]
    public async Task Uploads_placeholder_images_to_storage_when_reachable()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryImageStorage();
        await using var db = database.CreateContext();

        var result = await NewSeeder(db, storage, ImagesDirectory()).SeedAsync(ct);

        result.ImagesUploaded.Should().BeGreaterThan(0);
        result.ImagePathsOnly.Should().Be(0);
        storage.Paths.Should().HaveCount(result.ImagesUploaded);

        await using var read = database.CreateContext();
        var imagePaths = await read.ProductImages.AsNoTracking().Select(i => i.BlobPath).ToListAsync(ct);
        imagePaths.Should().OnlyContain(p => storage.Paths.Contains(p));
    }

    [Fact]
    public async Task Seeds_paths_only_when_storage_is_unreachable_and_stops_trying_after_the_first_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new UnreachableStorage();
        await using var db = database.CreateContext();

        var result = await NewSeeder(db, storage, ImagesDirectory()).SeedAsync(ct);

        result.Seeded.Should().BeTrue();
        result.ImagesUploaded.Should().Be(0);
        result.ImagePathsOnly.Should().BeGreaterThan(0);
        storage.Attempts.Should().Be(1);
        await using var read = database.CreateContext();
        (await read.ProductImages.CountAsync(ct)).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Seeds_paths_only_when_the_seed_images_directory_is_missing()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryImageStorage();
        await using var db = database.CreateContext();

        var result = await NewSeeder(db, storage, imagesDirectory: null).SeedAsync(ct);

        result.Seeded.Should().BeTrue();
        result.ImagesUploaded.Should().Be(0);
        storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task Seeding_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var storage = new InMemoryImageStorage();
        await using var db = database.CreateContext();
        var first = await NewSeeder(db, storage, ImagesDirectory()).SeedAsync(ct);
        var uploadedAfterFirst = storage.Paths.Count;

        await using var second = database.CreateContext();
        var again = await NewSeeder(second, storage, ImagesDirectory()).SeedAsync(ct);

        again.Seeded.Should().BeFalse();
        (await second.Products.CountAsync(ct)).Should().Be(first.Products);
        storage.Paths.Should().HaveCount(uploadedAfterFirst);
    }

    [Fact]
    public async Task Seeder_and_initializer_are_not_registered_outside_Development()
    {
        using var factory = new ApiFactory(database, environment: "Testing");
        using var client = factory.CreateClient();

        factory.Services.GetServices<IHostedService>().Should().NotContain(s => s is DevelopmentDataInitializer);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetService<DevelopmentSeeder>().Should().BeNull();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Initializer_in_Development_applies_migrations_and_seeds_once_when_enabled()
    {
        var ct = TestContext.Current.CancellationToken;
        var overrides = new Dictionary<string, string?> { [DevelopmentDataInitializer.EnabledKey] = "true" };
        using var factory = new ApiFactory(database, overrides, environment: "Development");
        using var client = factory.CreateClient();

        await WaitForAsync(async () =>
        {
            await using var db = database.CreateContext();
            return await db.Products.AnyAsync(ct);
        });

        await using var read = database.CreateContext();
        (await read.Products.CountAsync(ct)).Should().BeGreaterThanOrEqualTo(40);
        factory.ImageStorage.Paths.Should().NotBeEmpty("the in-memory storage was reachable");
    }

    [Fact]
    public async Task Initializer_does_nothing_in_Development_when_disabled()
    {
        var ct = TestContext.Current.CancellationToken;
        using var factory = new ApiFactory(database, environment: "Development"); // Enabled=false from the factory
        using var client = factory.CreateClient();

        await Task.Delay(TimeSpan.FromSeconds(2), ct);

        await using var read = database.CreateContext();
        (await read.Products.CountAsync(ct)).Should().Be(0);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(500, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The Development initializer did not seed in time.");
    }
}
