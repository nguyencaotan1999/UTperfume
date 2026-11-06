using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UTperfume.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "Brand",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(200)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    LogoBlobPath = table.Column<string>(type: "varchar(300)", nullable: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brand", x => x.Id);
                    table.CheckConstraint("CK_Brand_SlugFormat", "[Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2 AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "Category",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(200)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", nullable: true),
                    ImageBlobPath = table.Column<string>(type: "varchar(300)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.CheckConstraint("CK_Category_SlugFormat", "[Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2 AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "Concentration",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<short>(type: "smallint", nullable: false),
                    Code = table.Column<string>(type: "varchar(20)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Concentration", x => x.Id);
                    table.CheckConstraint("CK_Concentration_CodeFormat", "[Code] NOT LIKE '%[^A-Z_]%' COLLATE Latin1_General_BIN2");
                });

            migrationBuilder.CreateTable(
                name: "FragranceNote",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(200)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FragranceNote", x => x.Id);
                    table.CheckConstraint("CK_FragranceNote_SlugFormat", "[Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2 AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'");
                });

            migrationBuilder.CreateTable(
                name: "HomeBanner",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(150)", nullable: false),
                    Subtitle = table.Column<string>(type: "nvarchar(300)", nullable: true),
                    ImageBlobPath = table.Column<string>(type: "varchar(300)", nullable: false),
                    AltText = table.Column<string>(type: "nvarchar(250)", nullable: false),
                    LinkUrl = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HomeBanner", x => x.Id);
                    table.CheckConstraint("CK_HomeBanner_LinkUrlRelative", "[LinkUrl] IS NULL OR ([LinkUrl] LIKE '/%' AND [LinkUrl] NOT LIKE '//%')");
                    table.CheckConstraint("CK_HomeBanner_Window", "[StartsAtUtc] IS NULL OR [EndsAtUtc] IS NULL OR [EndsAtUtc] > [StartsAtUtc]");
                });

            migrationBuilder.CreateTable(
                name: "Product",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BrandId = table.Column<int>(type: "int", nullable: false),
                    ConcentrationId = table.Column<short>(type: "smallint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", nullable: false),
                    Slug = table.Column<string>(type: "varchar(200)", nullable: false),
                    ShortDescription = table.Column<string>(type: "nvarchar(500)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    PublishedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    IsFeatured = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsBestSeller = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    MetaTitle = table.Column<string>(type: "nvarchar(200)", nullable: true),
                    MetaDescription = table.Column<string>(type: "nvarchar(320)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                    table.CheckConstraint("CK_Product_PublishedAtRequired", "[Status] <> 1 OR [PublishedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_Product_SlugFormat", "[Slug] NOT LIKE '%[^a-z0-9-]%' COLLATE Latin1_General_BIN2 AND [Slug] NOT LIKE '-%' AND [Slug] NOT LIKE '%-' AND [Slug] NOT LIKE '%--%'");
                    table.CheckConstraint("CK_Product_StatusValid", "[Status] IN (0,1,2)");
                    table.ForeignKey(
                        name: "FK_Product_Brand",
                        column: x => x.BrandId,
                        principalSchema: "catalog",
                        principalTable: "Brand",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Product_Concentration",
                        column: x => x.ConcentrationId,
                        principalSchema: "catalog",
                        principalTable: "Concentration",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ProductCategory",
                schema: "catalog",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCategory", x => new { x.ProductId, x.CategoryId });
                    table.ForeignKey(
                        name: "FK_ProductCategory_Category",
                        column: x => x.CategoryId,
                        principalSchema: "catalog",
                        principalTable: "Category",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductCategory_Product",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductImage",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BlobPath = table.Column<string>(type: "varchar(300)", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(50)", nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", nullable: true),
                    AltText = table.Column<string>(type: "nvarchar(250)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductImage", x => x.Id);
                    table.CheckConstraint("CK_ProductImage_ContentType", "[ContentType] IN ('image/jpeg','image/png','image/webp')");
                    table.CheckConstraint("CK_ProductImage_SizeBytes", "[SizeBytes] > 0 AND [SizeBytes] <= 5242880");
                    table.ForeignKey(
                        name: "FK_ProductImage_Product",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductNote",
                schema: "catalog",
                columns: table => new
                {
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    NoteType = table.Column<byte>(type: "tinyint", nullable: false),
                    FragranceNoteId = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductNote", x => new { x.ProductId, x.NoteType, x.FragranceNoteId });
                    table.CheckConstraint("CK_ProductNote_NoteTypeValid", "[NoteType] IN (1,2,3)");
                    table.ForeignKey(
                        name: "FK_ProductNote_FragranceNote",
                        column: x => x.FragranceNoteId,
                        principalSchema: "catalog",
                        principalTable: "FragranceNote",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ProductNote_Product",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Product",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariant",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Sku = table.Column<string>(type: "varchar(64)", nullable: false),
                    VolumeMl = table.Column<decimal>(type: "decimal(6,1)", nullable: false),
                    Price = table.Column<long>(type: "bigint", nullable: false),
                    OriginalPrice = table.Column<long>(type: "bigint", nullable: true),
                    StockQuantity = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariant", x => x.Id);
                    table.CheckConstraint("CK_ProductVariant_OriginalPriceGreater", "[OriginalPrice] IS NULL OR [OriginalPrice] > [Price]");
                    table.CheckConstraint("CK_ProductVariant_PriceNonNegative", "[Price] >= 0");
                    table.CheckConstraint("CK_ProductVariant_StockNonNegative", "[StockQuantity] >= 0");
                    table.CheckConstraint("CK_ProductVariant_VolumeRange", "[VolumeMl] > 0 AND [VolumeMl] <= 5000");
                    table.ForeignKey(
                        name: "FK_ProductVariant_Product",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Product",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Concentration",
                columns: new[] { "Id", "Code", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { (short)1, "PARFUM", true, "Parfum", (short)1 },
                    { (short)2, "EXTRAIT", true, "Extrait de Parfum", (short)2 },
                    { (short)3, "EDP", true, "Eau de Parfum", (short)3 },
                    { (short)4, "EDT", true, "Eau de Toilette", (short)4 },
                    { (short)5, "EDC", true, "Eau de Cologne", (short)5 },
                    { (short)6, "EAU_FRAICHE", true, "Eau Fraîche", (short)6 }
                });

            migrationBuilder.CreateIndex(
                name: "UX_Brand_Name",
                schema: "catalog",
                table: "Brand",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Brand_Slug",
                schema: "catalog",
                table: "Brand",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Category_Name",
                schema: "catalog",
                table: "Category",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Category_Slug",
                schema: "catalog",
                table: "Category",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Concentration_Code",
                schema: "catalog",
                table: "Concentration",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FragranceNote_Name",
                schema: "catalog",
                table: "FragranceNote",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_FragranceNote_Slug",
                schema: "catalog",
                table: "FragranceNote",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HomeBanner_IsActive_SortOrder",
                schema: "catalog",
                table: "HomeBanner",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_BrandId",
                schema: "catalog",
                table: "Product",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_ConcentrationId",
                schema: "catalog",
                table: "Product",
                column: "ConcentrationId");

            migrationBuilder.CreateIndex(
                name: "IX_Product_Status_Name",
                schema: "catalog",
                table: "Product",
                columns: new[] { "Status", "Name", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Status_PublishedAtUtc",
                schema: "catalog",
                table: "Product",
                columns: new[] { "Status", "PublishedAtUtc", "Id" },
                descending: new[] { false, true, false })
                .Annotation("SqlServer:Include", new[] { "BrandId", "ConcentrationId", "Name", "Slug", "IsFeatured", "IsBestSeller" });

            migrationBuilder.CreateIndex(
                name: "UX_Product_Slug",
                schema: "catalog",
                table: "Product",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductCategory_CategoryId_ProductId",
                schema: "catalog",
                table: "ProductCategory",
                columns: new[] { "CategoryId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductImage_ProductId_SortOrder",
                schema: "catalog",
                table: "ProductImage",
                columns: new[] { "ProductId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductImage_BlobPath",
                schema: "catalog",
                table: "ProductImage",
                column: "BlobPath",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductImage_ProductId_Primary",
                schema: "catalog",
                table: "ProductImage",
                column: "ProductId",
                unique: true,
                filter: "[IsPrimary] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProductNote_FragranceNoteId",
                schema: "catalog",
                table: "ProductNote",
                column: "FragranceNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_ProductId_IsActive",
                schema: "catalog",
                table: "ProductVariant",
                columns: new[] { "ProductId", "IsActive" })
                .Annotation("SqlServer:Include", new[] { "Price", "OriginalPrice", "VolumeMl", "StockQuantity" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariant_VolumeMl",
                schema: "catalog",
                table: "ProductVariant",
                column: "VolumeMl")
                .Annotation("SqlServer:Include", new[] { "ProductId", "IsActive", "Price" });

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariant_ProductId_VolumeMl",
                schema: "catalog",
                table: "ProductVariant",
                columns: new[] { "ProductId", "VolumeMl" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_ProductVariant_Sku",
                schema: "catalog",
                table: "ProductVariant",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HomeBanner",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductCategory",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductImage",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductNote",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductVariant",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Category",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "FragranceNote",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Product",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Brand",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Concentration",
                schema: "catalog");
        }
    }
}
