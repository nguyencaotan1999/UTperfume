using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using UTperfume.Api.Endpoints.Admin;
using UTperfume.Api.Infrastructure;
using UTperfume.Application.Common;
using UTperfume.Application.Domain;
using UTperfume.Infrastructure.Persistence;

namespace UTperfume.IntegrationTests.Infrastructure;

public sealed record TestBrandBody(string? Name, string? Slug, string? RowVersion);

public sealed record TestVariantBody(string Sku, decimal VolumeMl);

public sealed record TestWriteResponse(int Id, string RowVersion);

/// <summary>
/// Throwaway admin endpoints that exist only in the test host (P4-08). They exercise the shared write foundation the way
/// a real resource endpoint will: row version, session save with audit stamping and error mapping, audit event, cache eviction.
/// </summary>
internal sealed class TestAdminRoutes : IAdminRouteContributor
{
    private static int _uploadFormReads;

    /// <summary>How many times the multipart test route ran its handler (and so read the form).</summary>
    public static int UploadFormReads => Volatile.Read(ref _uploadFormReads);

    private static int _jsonBindRuns;
    private static int _fileBindRuns;

    /// <summary>How many times the JSON-bound test route's handler ran.</summary>
    public static int JsonBindRuns => Volatile.Read(ref _jsonBindRuns);

    /// <summary>How many times the IFormFile-bound test route's handler ran.</summary>
    public static int FileBindRuns => Volatile.Read(ref _fileBindRuns);

    public void Map(RouteGroupBuilder admin)
    {
        var group = admin.MapGroup("/test");

        group.MapGet("/brands/{id:int}", async (int id, AppDbContext db, CancellationToken ct) =>
        {
            var brand = await db.Brands.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, ct);
            return brand is null
                ? ResultMapping.NotFound()
                : TypedResults.Ok(new TestWriteResponse(brand.Id, AdminRowVersion.Encode(brand.RowVersion)));
        });

        group.MapPost("/brands", async (
            TestBrandBody body, AppDbContext db, IAdminWriteSession session, IAdminAudit audit,
            IPublicCatalogCache cache, CancellationToken ct) =>
        {
            var brand = new Brand { Name = body.Name ?? string.Empty, Slug = body.Slug ?? string.Empty };
            db.Brands.Add(brand);
            if (await session.SaveChangesAsync(ct) is { } error)
            {
                return error.ToProblem();
            }

            audit.Write(AdminAuditActions.Create, nameof(Brand), brand.Id);
            await cache.EvictAsync(ct);
            return Results.Created($"/api/v1/admin/test/brands/{brand.Id}",
                new TestWriteResponse(brand.Id, AdminRowVersion.Encode(brand.RowVersion)));
        });

        group.MapPut("/brands/{id:int}", async (
            int id, TestBrandBody body, AppDbContext db, IAdminWriteSession session, IAdminAudit audit,
            IPublicCatalogCache cache, CancellationToken ct) =>
        {
            var version = AdminRowVersion.Decode(body.RowVersion);
            if (!version.IsSuccess)
            {
                return version.Error!.ToProblem();
            }

            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (brand is null)
            {
                return ResultMapping.NotFound();
            }

            session.ExpectRowVersion(brand, version.Value!);
            if (body.Name is not null)
            {
                brand.Name = body.Name;
            }

            if (await session.SaveChangesAsync(ct) is { } error)
            {
                return error.ToProblem();
            }

            audit.Write(AdminAuditActions.Update, nameof(Brand), brand.Id);
            await cache.EvictAsync(ct);
            return Results.Ok(new TestWriteResponse(brand.Id, AdminRowVersion.Encode(brand.RowVersion)));
        });

        group.MapDelete("/brands/{id:int}", async (
            int id, string? rowVersion, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            var version = AdminRowVersion.Decode(rowVersion);
            if (!version.IsSuccess)
            {
                return version.Error!.ToProblem();
            }

            var brand = await db.Brands.FirstOrDefaultAsync(b => b.Id == id, ct);
            if (brand is null)
            {
                return ResultMapping.NotFound();
            }

            session.ExpectRowVersion(brand, version.Value!);
            db.Brands.Remove(brand);
            return await session.SaveChangesAsync(ct) is { } error ? error.ToProblem() : Results.NoContent();
        });

        // Modifies without ExpectRowVersion: the session must refuse (M2).
        group.MapPut("/brands/{id:int}/unversioned", async (
            int id, TestBrandBody body, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            var brand = await db.Brands.FirstAsync(b => b.Id == id, ct);
            brand.Name = body.Name ?? brand.Name;
            return await session.SaveChangesAsync(ct) is { } error ? error.ToProblem() : Results.Ok();
        });

        // Explicit opt-out for server-side touches (M2).
        group.MapPut("/brands/{id:int}/server-side", async (
            int id, TestBrandBody body, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            var brand = await db.Brands.FirstAsync(b => b.Id == id, ct);
            brand.Name = body.Name ?? brand.Name;
            return await session.SaveServerSideChangesAsync(ct) is { } error ? error.ToProblem() : Results.Ok();
        });

        // Violates the unmapped UX_ProductImage_BlobPath on the second call (M5a).
        group.MapPost("/products/{productId:int}/images/fixed-path", async (
            int productId, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            db.ProductImages.Add(new ProductImage
            {
                ProductId = productId, BlobPath = "products/fixed/same.png", ContentType = "image/png", SizeBytes = 10,
            });
            return await session.SaveChangesAsync(ct) is { } error ? error.ToProblem() : Results.Ok();
        });

        // Violates UX_ProductImage_ProductId_Primary on the second call (M5b).
        group.MapPost("/products/{productId:int}/images/primary", async (
            int productId, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            db.ProductImages.Add(new ProductImage
            {
                ProductId = productId, BlobPath = $"products/{productId}/{Guid.NewGuid():N}.png",
                ContentType = "image/png", SizeBytes = 10, IsPrimary = true,
            });
            return await session.SaveChangesAsync(ct) is { } error ? error.ToProblem() : Results.Ok();
        });

        group.MapPost("/products/{productId:int}/variants", async (
            int productId, TestVariantBody body, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            var variant = new ProductVariant
            {
                ProductId = productId, Sku = body.Sku, VolumeMl = body.VolumeMl, Price = 1000, StockQuantity = 1,
            };
            db.ProductVariants.Add(variant);
            return await session.SaveChangesAsync(ct) is { } error
                ? error.ToProblem()
                : Results.Created($"/api/v1/admin/test/variants/{variant.Id}", new TestWriteResponse(variant.Id, AdminRowVersion.Encode(variant.RowVersion)));
        });

        // Multipart route (P4-03, AC-A-38): reads the form itself, so a counter proves the filter ran before the body was read.
        group.MapPost("/uploads", async (HttpRequest request, CancellationToken ct) =>
        {
            Interlocked.Increment(ref _uploadFormReads);
            var form = await request.ReadFormAsync(ct);
            return Results.Ok(new { files = form.Files.Count });
        });

        // JSON-bound route (M1): proves the token is checked before the body is bound; the counter shows the handler ran.
        group.MapPost("/json-bind", (TestBrandBody body) =>
        {
            Interlocked.Increment(ref _jsonBindRuns);
            return Results.Ok(new { body.Name });
        });

        // IFormFile-bound route (M1). DisableAntiforgery: the framework's own check is not the one under test.
        group.MapPost("/file-bind", (IFormFile file) =>
        {
            Interlocked.Increment(ref _fileBindRuns);
            return Results.Ok(new { file.Length });
        }).DisableAntiforgery();

        group.MapPost("/products/{productId:int}/images", async (
            int productId, AppDbContext db, IAdminWriteSession session, CancellationToken ct) =>
        {
            var image = new ProductImage
            {
                ProductId = productId, BlobPath = $"products/{productId}/{Guid.NewGuid():N}.png",
                ContentType = "image/png", SizeBytes = 10,
            };
            db.ProductImages.Add(image);
            return await session.SaveChangesAsync(ct) is { } error
                ? error.ToProblem()
                : Results.Created($"/api/v1/admin/test/images/{image.Id}", new { image.Id });
        });
    }
}
