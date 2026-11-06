using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.Infrastructure.Persistence;

public sealed record DevelopmentSeedResult(bool Seeded, int Products, int ImagesUploaded, int ImagePathsOnly);

/// <summary>
/// Loads a sample catalogue for local development (not a migration, so it never reaches production). The seed is
/// deterministic and idempotent: when any product exists it does nothing. Placeholder images are uploaded to blob
/// storage when it is reachable; otherwise only the blob paths are stored and the UI shows its placeholder.
/// </summary>
public sealed partial class DevelopmentSeeder(
    AppDbContext db,
    IImageStorage storage,
    TimeProvider clock,
    ILogger<DevelopmentSeeder> logger,
    string? seedImagesDirectory = null)
{
    private static readonly TimeSpan UploadProbeTimeout = TimeSpan.FromSeconds(10);

    private static readonly string[] BrandNames =
    [
        "Maison Aurel", "Nordhaven", "Saigon Atelier", "Lumière Noire", "Casa Verde", "Orient Sillage", "Atelier Brume",
        "Vieux Port",
    ];

    private const int InactiveBrandIndex = 7;

    private static readonly string[] CategoryNames = ["Men", "Women", "Unisex", "Niche", "Gift sets", "Summer", "Legacy"];

    private const int InactiveCategoryIndex = 6;

    private static readonly string[] NoteNames =
    [
        "Bergamot", "Lemon", "Grapefruit", "Pink Pepper", "Lavender", "Jasmine", "Rose", "Iris", "Neroli", "Cardamom",
        "Vetiver", "Sandalwood", "Cedar", "Patchouli", "Amber", "Vanilla", "Musk", "Oud", "Tonka Bean", "Leather",
        "Tuberose", "Orange Blossom", "Incense", "Black Pepper",
    ];

    private static readonly string[] ProductNames =
    [
        "Aube Dorée", "Nuit Étoilée", "Đêm Sài Gòn", "Hà Nội Mùa Thu", "Fjord", "Nordlys", "Sillage d'Orient",
        "Jardin Secret", "Cuir Fauve", "Brume du Matin", "Océan Pur", "Bois de Santal", "Rose Absolue", "Ambre Gris",
        "Lavande Royale", "Vétiver Noir", "Thé Blanc", "Figue Sauvage", "Épices Rares", "Neroli Soleil",
        "Đà Lạt Sương Sớm", "Muscade", "Santal Poudré", "Iris Pallida", "Oud Mystère", "Citron Vert", "Tabac Vanille",
        "Fleur d'Oranger", "Encens Blanc", "Poivre Noir", "Cèdre Rouge", "Mimosa", "Magnolia", "Tubéreuse Nuit",
        "Eau de Hué", "Pivoine", "Gardénia", "Réglisse",
    ];

    private static readonly short[] ConcentrationCycle = [3, 4, 1, 2, 3, 5, 6];

    private static readonly decimal[][] VolumeSets =
    [
        [50m, 100m], [30m, 50m, 100m], [100m], [50m, 100m, 200m], [30m, 100m],
    ];

    private static readonly int[] StockCycle = [0, 1, 3, 5, 6, 40];

    public async Task<DevelopmentSeedResult> SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(cancellationToken))
        {
            LogAlreadySeeded(logger);
            return new DevelopmentSeedResult(false, 0, 0, 0);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var images = SeedImages.Load(seedImagesDirectory);
        var uploader = new BlobUploader(storage, images.Count > 0, logger);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var concentrations = await db.Concentrations.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);

        var brands = new List<Brand>();
        for (var i = 0; i < BrandNames.Length; i++)
        {
            brands.Add(new Brand
            {
                Name = BrandNames[i],
                Slug = Slug.Generate(BrandNames[i]),
                Description = $"{BrandNames[i]} is a fictional house created for development data.",
                LogoBlobPath = await uploader.UploadAsync("brands", images.Logo, cancellationToken),
                IsFeatured = i is 0 or 1 or 2 or 3 or 5,
                DisplayOrder = i,
                IsActive = i != InactiveBrandIndex,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        var categories = CategoryNames.Select((name, i) => new Category
        {
            Name = name,
            Slug = Slug.Generate(name),
            Description = $"{name} fragrances.",
            DisplayOrder = i,
            IsActive = i != InactiveCategoryIndex,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        }).ToList();
        foreach (var category in categories)
        {
            category.ImageBlobPath = await uploader.UploadAsync("categories", images.Logo, cancellationToken);
        }

        var notes = NoteNames.Select(n => new FragranceNote { Name = n, Slug = Slug.Generate(n), CreatedAtUtc = now }).ToList();

        db.Brands.AddRange(brands);
        db.Categories.AddRange(categories);
        db.FragranceNotes.AddRange(notes);
        await db.SaveChangesAsync(cancellationToken);

        var products = new List<Product>();
        var usedSlugs = new HashSet<string>(StringComparer.Ordinal);
        var usedSkus = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < ProductNames.Length; i++)
        {
            var brand = brands[i % (BrandNames.Length - 1)];
            var concentration = concentrations[ConcentrationCycle[i % ConcentrationCycle.Length]];
            var product = NewProduct(i, ProductNames[i], brand, concentration, now, usedSlugs);
            product.PublishedAtUtc = now.AddDays(-(i * 2 + 1));
            product.Status = ProductStatus.Published;
            product.IsFeatured = i % 7 == 0;
            product.IsBestSeller = i % 6 == 1;
            AddVariants(product, i, VolumeSets[i % VolumeSets.Length], now, usedSkus, discountFirst: i % 5 == 0,
                allSoldOut: i % 9 == 4);
            AddCategoriesAndNotes(product, i, categories, notes);
            products.Add(product);
        }

        // Edge cases for visibility and state handling (BR-01, BR-03, BR-02).
        var edgeIndex = ProductNames.Length;
        var visibleBrand = brands[0];
        var edp = concentrations[3];

        var draft = NewProduct(edgeIndex, "Bản Thảo Draft", visibleBrand, edp, now, usedSlugs);
        AddVariants(draft, edgeIndex, [50m, 100m], now, usedSkus, false, false);
        products.Add(draft);

        var archived = NewProduct(edgeIndex + 1, "Archived Classic", visibleBrand, edp, now, usedSlugs);
        archived.Status = ProductStatus.Archived;
        archived.PublishedAtUtc = now.AddDays(-200);
        AddVariants(archived, edgeIndex + 1, [100m], now, usedSkus, false, false);
        products.Add(archived);

        var hiddenByBrand = NewProduct(edgeIndex + 2, "Port Wine", brands[InactiveBrandIndex], edp, now, usedSlugs);
        hiddenByBrand.Status = ProductStatus.Published;
        hiddenByBrand.PublishedAtUtc = now.AddDays(-10);
        AddVariants(hiddenByBrand, edgeIndex + 2, [50m], now, usedSkus, false, false);
        products.Add(hiddenByBrand);

        var noActiveVariant = NewProduct(edgeIndex + 3, "Sans Variante", visibleBrand, edp, now, usedSlugs);
        noActiveVariant.Status = ProductStatus.Published;
        noActiveVariant.PublishedAtUtc = now.AddDays(-12);
        AddVariants(noActiveVariant, edgeIndex + 3, [50m], now, usedSkus, false, false);
        noActiveVariant.Variants.ForEach(v => v.IsActive = false);
        products.Add(noActiveVariant);

        db.Products.AddRange(products);
        await db.SaveChangesAsync(cancellationToken);

        // Images need the generated product ids for the blob path. The "Sans Variante" product has none on purpose.
        var withoutImages = new HashSet<string>(StringComparer.Ordinal) { noActiveVariant.Slug };
        for (var i = 0; i < products.Count; i++)
        {
            var product = products[i];
            if (withoutImages.Contains(product.Slug))
            {
                continue;
            }

            for (var n = 0; n < 2; n++)
            {
                var image = images.ProductImageAt(i + n);
                var path = await uploader.UploadAsync($"products/{product.Id}", image, cancellationToken);
                db.ProductImages.Add(new ProductImage
                {
                    ProductId = product.Id,
                    BlobPath = path,
                    ContentType = "image/png",
                    SizeBytes = image?.Length ?? 512,
                    Width = 96,
                    Height = 96,
                    OriginalFileName = "placeholder.png",
                    AltText = n == 0 ? product.Name : $"{product.Name} (detail)",
                    SortOrder = n,
                    IsPrimary = n == 0,
                    CreatedAtUtc = now,
                });
            }
        }

        string[] bannerTitles = ["New season", "Signature scents", "Gift season"];
        for (var i = 0; i < bannerTitles.Length; i++)
        {
            db.HomeBanners.Add(new HomeBanner
            {
                Title = bannerTitles[i],
                Subtitle = "Development sample banner",
                ImageBlobPath = await uploader.UploadAsync("banners", images.Banner, cancellationToken),
                AltText = bannerTitles[i],
                LinkUrl = "/perfumes",
                SortOrder = i,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var result = new DevelopmentSeedResult(true, products.Count, uploader.Uploaded, uploader.PathsOnly);
        LogSeeded(logger, result.Products, result.ImagesUploaded, result.ImagePathsOnly);
        return result;
    }

    private static Product NewProduct(
        int index, string name, Brand brand, Concentration concentration, DateTime now, HashSet<string> usedSlugs)
    {
        var slug = Slug.Generate($"{brand.Name} {name} {concentration.Code}");
        if (!usedSlugs.Add(slug))
        {
            slug = $"{slug}-{index}";
            usedSlugs.Add(slug);
        }

        return new Product
        {
            Brand = brand,
            ConcentrationId = concentration.Id,
            Name = name,
            Slug = slug,
            ShortDescription = $"{name} by {brand.Name}: a sample {concentration.Name.ToLowerInvariant()} for development.",
            Description = $"{name} is development sample data. Any resemblance to a real fragrance is coincidental.",
            Status = ProductStatus.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private void AddVariants(
        Product product, int index, decimal[] volumes, DateTime now, HashSet<string> usedSkus, bool discountFirst,
        bool allSoldOut)
    {
        // The reference price scales with volume (factor ^0.7). The base keeps the largest variant at or below
        // 8,900,000 VND and the smallest at or above 1,250,000 VND.
        var smallest = volumes.Min();
        var largestFactor = Math.Pow((double)(volumes.Max() / smallest), 0.7);
        var upper = Math.Max(1_250_000d, 8_900_000d / largestFactor);
        var fraction = (index * 37 % 100) / 99d;
        var basePrice = 1_250_000d + fraction * (upper - 1_250_000d);

        for (var v = 0; v < volumes.Length; v++)
        {
            var factor = Math.Pow((double)(volumes[v] / smallest), 0.7);
            var price = Math.Min(8_900_000L, RoundTo10K(basePrice * factor));
            var sku = $"{string.Concat(product.Brand.Name.Where(char.IsAsciiLetter).Take(3)).ToUpperInvariant()}-{index:00}-" +
                      volumes[v].ToString("0.#", CultureInfo.InvariantCulture);
            if (!VariantRules.IsValidSku(sku) || !usedSkus.Add(sku))
            {
                throw new InvalidOperationException($"Seed produced an invalid or duplicate SKU '{sku}'.");
            }

            var stock = allSoldOut ? 0 : StockCycle[(index + v) % StockCycle.Length];
            product.Variants.Add(new ProductVariant
            {
                Sku = sku,
                VolumeMl = volumes[v],
                Price = price,
                OriginalPrice = discountFirst && v == 0 ? RoundTo10K(price * 1.2) : null,
                StockQuantity = stock,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
        }
    }

    private static void AddCategoriesAndNotes(Product product, int index, List<Category> categories, List<FragranceNote> notes)
    {
        var active = categories.Where(c => c.IsActive).ToList();
        product.Categories.Add(new ProductCategory { Category = active[index % active.Count] });
        if (index % 3 == 0)
        {
            product.Categories.Add(new ProductCategory { Category = active[(index + 2) % active.Count] });
        }

        if (index % 10 == 5)
        {
            // Linked to an inactive category only: the product stays visible (BR-02).
            product.Categories.Clear();
            product.Categories.Add(new ProductCategory { Category = categories[InactiveCategoryIndex] });
        }

        var tiers = new[] { NoteType.Top, NoteType.Heart, NoteType.Base };
        for (var t = 0; t < tiers.Length; t++)
        {
            for (var n = 0; n < 2; n++)
            {
                product.Notes.Add(new ProductNote
                {
                    NoteType = tiers[t],
                    FragranceNote = notes[(index * 3 + t * 7 + n * 5) % notes.Count],
                    SortOrder = (short)n,
                });
            }
        }
    }

    private static long RoundTo10K(double value) => (long)Math.Round(value / 10_000d, MidpointRounding.AwayFromZero) * 10_000;

    /// <summary>Walks up from <paramref name="startDirectory"/> to find <c>tools/seed-images</c>.</summary>
    public static string? FindSeedImagesDirectory(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "seed-images");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Development seed skipped: the catalogue already has products.")]
    private static partial void LogAlreadySeeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Development seed completed: {Products} products, {Uploaded} images uploaded, {PathsOnly} image paths without a blob.")]
    private static partial void LogSeeded(ILogger logger, int products, int uploaded, int pathsOnly);

    private sealed class SeedImages
    {
        private readonly byte[][] _products;

        private SeedImages(byte[][] products, byte[]? logo, byte[]? banner)
        {
            _products = products;
            Logo = logo;
            Banner = banner;
        }

        public int Count => _products.Length;

        public byte[]? Logo { get; }

        public byte[]? Banner { get; }

        public byte[]? ProductImageAt(int i) => _products.Length == 0 ? null : _products[i % _products.Length];

        public static SeedImages Load(string? directory)
        {
            if (directory is null || !Directory.Exists(directory))
            {
                return new SeedImages([], null, null);
            }

            var products = Directory.EnumerateFiles(directory, "placeholder-*.png").Order().Select(File.ReadAllBytes).ToArray();
            var banner = Path.Combine(directory, "banner.png");
            return new SeedImages(
                products,
                products.Length > 0 ? products[0] : null,
                File.Exists(banner) ? File.ReadAllBytes(banner) : null);
        }
    }

    /// <summary>
    /// Uploads best effort. The first failure (for example Azurite not running) switches to paths-only for the
    /// rest of the run, so an unreachable storage costs one short wait.
    /// </summary>
    private sealed partial class BlobUploader(IImageStorage storage, bool hasImages, ILogger logger)
    {
        private bool _enabled = hasImages;

        public int Uploaded { get; private set; }

        public int PathsOnly { get; private set; }

        public async Task<string> UploadAsync(string folder, byte[]? content, CancellationToken cancellationToken)
        {
            var path = $"{folder}/{Guid.NewGuid():N}.png";
            if (!_enabled || content is null)
            {
                PathsOnly++;
                return path;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (Uploaded == 0)
                {
                    timeout.CancelAfter(UploadProbeTimeout);
                }

                await using var stream = new MemoryStream(content, writable: false);
                await storage.UploadAsync(path, stream, "image/png", timeout.Token);
                Uploaded++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _enabled = false;
                PathsOnly++;
                LogUploadsDisabled(logger, ex.GetType().Name);
            }

            return path;
        }

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Image storage is not reachable ({ExceptionType}); seeding image paths only.")]
        private static partial void LogUploadsDisabled(ILogger logger, string exceptionType);
    }
}
