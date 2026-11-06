using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using UTperfume.Application.Domain;
using UTperfume.IntegrationTests.Infrastructure;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.IntegrationTests.Persistence;

/// <summary>Constraints from 0002 section 3, tested against the migrated database (real SQL Server).</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogSchemaTests(TestDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async ValueTask InitializeAsync() => await database.ResetCatalogAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static Brand NewBrand(string slug = "brand-a") =>
        new() { Name = slug, Slug = slug, CreatedAtUtc = Now, UpdatedAtUtc = Now };

    private static Product NewProduct(Brand brand, string slug = "prod-a", ProductStatus status = ProductStatus.Draft,
        DateTime? publishedAt = null) =>
        new()
        {
            Brand = brand, ConcentrationId = 3, Name = slug, Slug = slug, Status = status,
            PublishedAtUtc = publishedAt, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };

    private static ProductVariant NewVariant(string sku = "SKU-1", decimal volume = 50m, long price = 1_000_000,
        long? original = null, int stock = 3) =>
        new()
        {
            Sku = sku, VolumeMl = volume, Price = price, OriginalPrice = original, StockQuantity = stock,
            CreatedAtUtc = Now, UpdatedAtUtc = Now,
        };

    private static ProductImage NewImage(string blobPath, string contentType = "image/png", int size = 1000,
        bool primary = false) =>
        new() { BlobPath = blobPath, ContentType = contentType, SizeBytes = size, IsPrimary = primary, CreatedAtUtc = Now };

    private async Task<(AppDbContext Context, Product Product)> SeedProductAsync()
    {
        var context = database.CreateContext();
        var product = NewProduct(NewBrand());
        context.Products.Add(product);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return (context, product);
    }

    private static async Task<SqlException> ShouldViolateAsync(AppDbContext context, string constraint)
    {
        var act = () => context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var thrown = await act.Should().ThrowAsync<DbUpdateException>();
        var sql = thrown.Which.InnerException.Should().BeOfType<SqlException>().Subject;
        sql.Message.Should().Contain(constraint);
        return sql;
    }


    [Theory]
    [InlineData("xyz")]
    [InlineData("Xyz")]
    [InlineData("EDP-2")]
    [InlineData("EDP 2")]
    public async Task Concentration_code_must_be_upper_case_letters_and_underscore(string code)
    {
        await using var context = database.CreateContext();
        context.Concentrations.Add(new Concentration { Id = 99, Code = code, Name = "Bad", SortOrder = 99 });

        await ShouldViolateAsync(context, "CK_Concentration_CodeFormat");
    }
    [Fact]
    public async Task Concentration_is_seeded_with_the_six_stable_rows()
    {
        await using var context = database.CreateContext();

        var rows = await context.Concentrations.AsNoTracking().OrderBy(c => c.Id).ToListAsync(TestContext.Current.CancellationToken);

        rows.Select(r => (r.Id, r.Code)).Should().Equal(
            ((short)1, "PARFUM"), ((short)2, "EXTRAIT"), ((short)3, "EDP"),
            ((short)4, "EDT"), ((short)5, "EDC"), ((short)6, "EAU_FRAICHE"));
        rows.Should().OnlyContain(r => r.IsActive);
    }

    [Fact]
    public async Task Migration_applied_all_tables_and_the_filtered_unique_index()
    {
        await using var context = database.CreateContext();
        var pending = await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);
        pending.Should().BeEmpty();

        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id
                    WHERE s.name = 'catalog'),
                   (SELECT COUNT(*) FROM sys.indexes WHERE name = 'UX_ProductImage_ProductId_Primary'
                    AND has_filter = 1 AND is_unique = 1)
            """;
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        (await reader.ReadAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
        reader.GetInt32(0).Should().Be(10);
        reader.GetInt32(1).Should().Be(1);
    }

    [Fact]
    public async Task Valid_catalogue_row_graph_round_trips_with_bigint_money_and_one_decimal_volume()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var write = database.CreateContext())
        {
            var product = NewProduct(NewBrand(), "round-trip", ProductStatus.Published, Now);
            product.Variants.Add(NewVariant("RT-100", 100.5m, 8_900_000_000, 8_900_000_001, 7));
            product.Images.Add(NewImage("products/1/a.png", primary: true));
            write.Products.Add(product);
            await write.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        var variant = await read.ProductVariants.AsNoTracking().SingleAsync(ct);
        variant.Price.Should().Be(8_900_000_000);
        variant.OriginalPrice.Should().Be(8_900_000_001);
        variant.VolumeMl.Should().Be(100.5m);
        variant.StockQuantity.Should().Be(7);
        variant.RowVersion.Should().HaveCount(8);
    }

    [Fact]
    public async Task Explicit_false_and_zero_are_stored_not_replaced_by_database_defaults()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var write = database.CreateContext())
        {
            var brand = NewBrand();
            brand.IsActive = false;
            var product = NewProduct(brand, "defaults");
            var variant = NewVariant("DEF-1", stock: 0);
            variant.IsActive = false;
            product.Variants.Add(variant);
            write.Products.Add(product);
            await write.SaveChangesAsync(ct);
        }

        await using var read = database.CreateContext();
        (await read.Brands.AsNoTracking().SingleAsync(ct)).IsActive.Should().BeFalse();
        var stored = await read.ProductVariants.AsNoTracking().SingleAsync(ct);
        stored.IsActive.Should().BeFalse();
        stored.StockQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected_case_insensitively()
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            context.ProductVariants.AddRange(
                new ProductVariant { ProductId = product.Id, Sku = "DUP-1", VolumeMl = 30, Price = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now },
                new ProductVariant { ProductId = product.Id, Sku = "dup-1", VolumeMl = 50, Price = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now });

            await ShouldViolateAsync(context, "UX_ProductVariant_Sku");
        }
    }

    [Fact]
    public async Task Duplicate_volume_within_one_product_is_rejected_but_allowed_across_products()
    {
        var ct = TestContext.Current.CancellationToken;
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            var other = NewProduct(product.Brand, "prod-b");
            other.Variants.Add(NewVariant("OTHER-50", 50m));
            context.Products.Add(other);
            await context.SaveChangesAsync(ct);

            context.ProductVariants.AddRange(
                new ProductVariant { ProductId = product.Id, Sku = "V-1", VolumeMl = 50, Price = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now },
                new ProductVariant { ProductId = product.Id, Sku = "V-2", VolumeMl = 50.0m, Price = 1, CreatedAtUtc = Now, UpdatedAtUtc = Now });

            await ShouldViolateAsync(context, "UX_ProductVariant_ProductId_VolumeMl");
        }
    }

    [Theory]
    [InlineData(1000L, 1000L)]
    [InlineData(1000L, 999L)]
    public async Task OriginalPrice_not_greater_than_price_is_rejected(long price, long original)
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            context.ProductVariants.Add(new ProductVariant
            {
                ProductId = product.Id, Sku = "OP-1", VolumeMl = 50, Price = price, OriginalPrice = original,
                CreatedAtUtc = Now, UpdatedAtUtc = Now,
            });

            await ShouldViolateAsync(context, "CK_ProductVariant_OriginalPriceGreater");
        }
    }

    [Fact]
    public async Task OriginalPrice_one_dong_above_price_is_accepted()
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            context.ProductVariants.Add(new ProductVariant
            {
                ProductId = product.Id, Sku = "OP-OK", VolumeMl = 50, Price = 1000, OriginalPrice = 1001,
                CreatedAtUtc = Now, UpdatedAtUtc = Now,
            });

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(-1L, 0, 50.0, "CK_ProductVariant_PriceNonNegative")]
    [InlineData(1L, -1, 50.0, "CK_ProductVariant_StockNonNegative")]
    [InlineData(1L, 0, 0.0, "CK_ProductVariant_VolumeRange")]
    [InlineData(1L, 0, 5000.1, "CK_ProductVariant_VolumeRange")]
    public async Task Variant_check_constraints_reject_invalid_values(long price, int stock, double volume, string constraint)
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            context.ProductVariants.Add(new ProductVariant
            {
                ProductId = product.Id, Sku = "CK-1", VolumeMl = (decimal)volume, Price = price, StockQuantity = stock,
                CreatedAtUtc = Now, UpdatedAtUtc = Now,
            });

            await ShouldViolateAsync(context, constraint);
        }
    }

    [Fact]
    public async Task Two_primary_images_for_one_product_are_rejected_but_one_per_product_is_fine()
    {
        var ct = TestContext.Current.CancellationToken;
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            var other = NewProduct(product.Brand, "prod-b");
            other.Images.Add(NewImage("products/2/x.png", primary: true));
            product.Images.Add(NewImage("products/1/a.png", primary: true));
            product.Images.Add(NewImage("products/1/b.png", primary: false));
            product.Images.Add(NewImage("products/1/c.png", primary: false));
            context.Products.Add(other);
            await context.SaveChangesAsync(ct);

            context.ProductImages.Add(new ProductImage
            {
                ProductId = product.Id, BlobPath = "products/1/d.png", ContentType = "image/png",
                SizeBytes = 10, IsPrimary = true, CreatedAtUtc = Now,
            });

            await ShouldViolateAsync(context, "UX_ProductImage_ProductId_Primary");
        }
    }

    [Fact]
    public async Task Duplicate_blob_path_is_rejected()
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            product.Images.Add(NewImage("products/1/same.png"));
            product.Images.Add(NewImage("products/1/same.png"));

            await ShouldViolateAsync(context, "UX_ProductImage_BlobPath");
        }
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("")]
    public async Task Invalid_image_content_type_is_rejected(string contentType)
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            product.Images.Add(NewImage("products/1/x.bin", contentType));

            await ShouldViolateAsync(context, "CK_ProductImage_ContentType");
        }
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public async Task Allowed_image_content_types_are_accepted(string contentType)
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            product.Images.Add(NewImage($"products/1/{Guid.NewGuid():N}.img", contentType));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5_242_881)]
    public async Task Image_size_outside_1_to_5MB_is_rejected(int size)
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            product.Images.Add(NewImage("products/1/size.png", size: size));

            await ShouldViolateAsync(context, "CK_ProductImage_SizeBytes");
        }
    }

    [Fact]
    public async Task Image_size_boundaries_1_and_5MB_are_accepted()
    {
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            product.Images.Add(NewImage("products/1/min.png", size: 1));
            product.Images.Add(NewImage("products/1/max.png", size: 5_242_880));

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
    }

    [Theory]
    [InlineData("Upper")]
    [InlineData("-lead")]
    [InlineData("trail-")]
    [InlineData("dou--ble")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("dốt")]
    public async Task Slug_format_check_rejects_invalid_slugs(string slug)
    {
        await using var context = database.CreateContext();
        context.Brands.Add(new Brand { Name = "n-" + Guid.NewGuid().ToString("N"), Slug = slug, CreatedAtUtc = Now, UpdatedAtUtc = Now });

        await ShouldViolateAsync(context, "CK_Brand_SlugFormat");
    }

    [Fact]
    public async Task Duplicate_slug_is_rejected_case_insensitively()
    {
        await using var context = database.CreateContext();
        context.Brands.Add(new Brand { Name = "One", Slug = "same", CreatedAtUtc = Now, UpdatedAtUtc = Now });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.Brands.Add(new Brand { Name = "Two", Slug = "same", CreatedAtUtc = Now, UpdatedAtUtc = Now });

        await ShouldViolateAsync(context, "UX_Brand_Slug");
    }

    [Fact]
    public async Task Published_product_requires_PublishedAtUtc()
    {
        await using var context = database.CreateContext();
        context.Products.Add(NewProduct(NewBrand(), "pub", ProductStatus.Published, publishedAt: null));

        await ShouldViolateAsync(context, "CK_Product_PublishedAtRequired");
    }

    [Fact]
    public async Task Invalid_product_status_value_is_rejected()
    {
        await using var context = database.CreateContext();
        context.Products.Add(NewProduct(NewBrand(), "bad-status", (ProductStatus)9));

        await ShouldViolateAsync(context, "CK_Product_StatusValid");
    }

    [Theory]
    [InlineData("//evil.example")]
    [InlineData("https://evil.example")]
    [InlineData("javascript:alert(1)")]
    public async Task Banner_link_must_be_site_relative(string link)
    {
        await using var context = database.CreateContext();
        context.HomeBanners.Add(new HomeBanner
        {
            Title = "t", ImageBlobPath = "banners/a.png", AltText = "a", LinkUrl = link,
            CreatedAtUtc = Now, UpdatedAtUtc = Now,
        });

        await ShouldViolateAsync(context, "CK_HomeBanner_LinkUrlRelative");
    }

    [Fact]
    public async Task Banner_window_must_end_after_it_starts()
    {
        await using var context = database.CreateContext();
        context.HomeBanners.Add(new HomeBanner
        {
            Title = "t", ImageBlobPath = "banners/a.png", AltText = "a", LinkUrl = "/perfumes",
            StartsAtUtc = Now, EndsAtUtc = Now, CreatedAtUtc = Now, UpdatedAtUtc = Now,
        });

        await ShouldViolateAsync(context, "CK_HomeBanner_Window");
    }

    [Fact]
    public async Task Deleting_a_product_cascades_to_images_notes_and_category_links_but_variants_block_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var context = database.CreateContext();
        var product = NewProduct(NewBrand());
        product.Images.Add(NewImage("products/1/c.png"));
        product.Notes.Add(new ProductNote
        {
            NoteType = NoteType.Top, SortOrder = 0,
            FragranceNote = new FragranceNote { Name = "Bergamot", Slug = "bergamot", CreatedAtUtc = Now },
        });
        product.Categories.Add(new ProductCategory
        {
            Category = new Category { Name = "Men", Slug = "men", CreatedAtUtc = Now, UpdatedAtUtc = Now },
        });
        context.Products.Add(product);
        await context.SaveChangesAsync(ct);

        // Server-side delete: tests the database cascade rules, not EF's client-side cascade.
        await context.Products.Where(p => p.Id == product.Id).ExecuteDeleteAsync(ct);

        (await context.ProductImages.CountAsync(ct)).Should().Be(0);
        (await context.ProductNotes.CountAsync(ct)).Should().Be(0);
        (await context.ProductCategories.CountAsync(ct)).Should().Be(0);

        var withVariant = NewProduct(product.Brand, "has-variant");
        withVariant.Variants.Add(NewVariant("BLOCK-1"));
        context.Products.Add(withVariant);
        await context.SaveChangesAsync(ct);

        var act = () => context.Products.Where(p => p.Id == withVariant.Id).ExecuteDeleteAsync(ct);
        await act.Should().ThrowAsync<SqlException>().WithMessage("*FK_ProductVariant_Product*");
    }

    [Fact]
    public async Task Same_note_may_appear_in_two_tiers_but_only_once_per_tier()
    {
        var ct = TestContext.Current.CancellationToken;
        var (context, product) = await SeedProductAsync();
        await using (context)
        {
            var note = new FragranceNote { Name = "Rose", Slug = "rose", CreatedAtUtc = Now };
            context.FragranceNotes.Add(note);
            await context.SaveChangesAsync(ct);

            context.ProductNotes.AddRange(
                new ProductNote { ProductId = product.Id, FragranceNoteId = note.Id, NoteType = NoteType.Top },
                new ProductNote { ProductId = product.Id, FragranceNoteId = note.Id, NoteType = NoteType.Heart });
            await context.SaveChangesAsync(ct);

            await using var other = database.CreateContext();
            other.ProductNotes.Add(new ProductNote { ProductId = product.Id, FragranceNoteId = note.Id, NoteType = NoteType.Top });
            await ShouldViolateAsync(other, "PK_ProductNote");
        }
    }

    [Fact]
    public async Task RowVersion_detects_a_concurrent_update()
    {
        var ct = TestContext.Current.CancellationToken;
        var (first, product) = await SeedProductAsync();
        await using (first)
        {
            await using var second = database.CreateContext();
            var stale = await second.Products.SingleAsync(p => p.Id == product.Id, ct);

            product.Name = "Changed by first";
            await first.SaveChangesAsync(ct);

            stale.Name = "Changed by second";
            var act = () => second.SaveChangesAsync(ct);
            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }
    }
}
