using Microsoft.Extensions.Logging.Abstractions;
using UTperfume.Application.Common;
using UTperfume.Application.Features.Admin.Images;
using UTperfume.Infrastructure.Storage;

namespace UTperfume.UnitTests.Images;

/// <summary>P4-06: the reusable upload service and the header reader (BR-A-23, BR-A-24, BR-A-26, BR-A-30; AC-A-31, AC-A-32, AC-A-35, AC-A-36).</summary>
public sealed class ImageUploadServiceTests
{
    private const int Max = 5_242_880;

    private readonly FakeStorage _storage = new();

    private ImageUploadService Service => new(_storage, NullLogger<ImageUploadService>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ImageUploadFile File(byte[] bytes, string? contentType, string? name = "a.jpg", long? length = null) =>
        new(new MemoryStream(bytes), length ?? bytes.Length, contentType, name);

    private static byte[] Jpeg(int length = 100) => Pad([0xFF, 0xD8, 0xFF, 0xE0], length);

    private static byte[] Png(int length = 100) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], length);

    private static byte[] WebP(int length = 100) =>
        Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8], length);

    private static byte[] Pad(byte[] head, int length)
    {
        var bytes = new byte[Math.Max(length, head.Length)];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    // ---- signature detection (BR-A-23) ----------------------------------------------------------------------------

    [Fact]
    public void BR_A_23_Detect_RecognisesTheThreeSignatures()
    {
        ImageHeader.Detect(Jpeg()).Should().Be(ImageHeader.Jpeg);
        ImageHeader.Detect(Png()).Should().Be(ImageHeader.Png);
        ImageHeader.Detect(WebP()).Should().Be(ImageHeader.WebP);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")]
    [InlineData("GIF89a......")]
    [InlineData("RIFF....WAVE")]
    [InlineData("RIFF")]
    [InlineData("")]
    public void BR_A_23_Detect_RejectsEverythingElse(string text) =>
        ImageHeader.Detect(System.Text.Encoding.ASCII.GetBytes(text)).Should().BeNull();

    [Fact]
    public void BR_A_23_Detect_RejectsHeicAndAvifBoxes() =>
        ImageHeader.Detect([0, 0, 0, 0x18, .. "ftypheic"u8, 0, 0, 0, 0]).Should().BeNull();

    // ---- dimensions (BR-A-30) ------------------------------------------------------------------------------------

    [Fact]
    public void BR_A_30_PngDimensions_AreReadFromIhdr()
    {
        List<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0x03, 0x20, 0, 0, 0x02, 0x58];

        ImageHeader.ReadDimensions(ImageHeader.Png, png.ToArray()).Should().Be((800, 600));
    }

    [Fact]
    public void BR_A_30_JpegDimensions_AreReadFromTheFirstStartOfFrame()
    {
        // SOI, APP0 (length 4, two payload bytes), SOF0: length 17, precision 8, height 480, width 640.
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x01, 0xE0, 0x02, 0x80, 0x03];

        ImageHeader.ReadDimensions(ImageHeader.Jpeg, jpeg).Should().Be((640, 480));
    }

    [Fact]
    public void BR_A_30_WebPDimensions_AreReadForVp8Vp8LAndVp8X()
    {
        var vp8 = Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8 "u8, 0, 0, 0, 0, 0, 0, 0, 0x9D, 0x01, 0x2A, 0x40, 0x01, 0xF0, 0x00], 40);
        var vp8l = Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8L"u8, 0, 0, 0, 0, 0x2F, 0x3F, 0x01, 0x00, 0x00], 40);
        var vp8x = Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. "VP8X"u8, 10, 0, 0, 0, 0, 0, 0, 0, 0x3F, 0x01, 0x00, 0xEF, 0x00, 0x00], 40);

        ImageHeader.ReadDimensions(ImageHeader.WebP, vp8).Should().Be((320, 240));
        ImageHeader.ReadDimensions(ImageHeader.WebP, vp8l).Should().Be((320, 1));
        ImageHeader.ReadDimensions(ImageHeader.WebP, vp8x).Should().Be((320, 240));
    }

    [Fact]
    public void BR_A_30_ADimensionlessOrTruncatedHeader_GivesNullNotAnError()
    {
        ImageHeader.ReadDimensions(ImageHeader.Png, Png(10)).Should().Be((null, null));
        ImageHeader.ReadDimensions(ImageHeader.Jpeg, Jpeg(6)).Should().Be((null, null));
        ImageHeader.ReadDimensions(ImageHeader.WebP, WebP(14)).Should().Be((null, null));
    }

    // ---- validation: AC-A10 / BR-A-23 / BR-A-24 ------------------------------------------------------------------

    [Fact]
    public async Task BR_A_24_AMissingFile_Is400OnTheFileField()
    {
        var result = await Service.ValidateAsync(null, Ct);

        result.Error!.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Details!.Should().ContainKey("file");
        _storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_24_AnEmptyFile_Is400OnTheFileField()
    {
        var result = await Service.ValidateAsync(File([], "image/jpeg"), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.Validation);
        result.Error.Details!.Should().ContainKey("file");
    }

    [Fact]
    public async Task BR_A_24_AFileWhoseClaimedLengthIsZeroButHasNoBytes_Is400()
    {
        var result = await Service.ValidateAsync(File([], "image/jpeg", length: 5), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.Validation);
    }

    [Fact]
    public async Task AC_A_32_ExactlyTheLimit_IsAccepted_AndOneByteMoreIs413()
    {
        var atLimit = await Service.ValidateAsync(File(Jpeg(Max), "image/jpeg"), Ct);
        var over = await Service.ValidateAsync(File(Jpeg(Max + 1), "image/jpeg"), Ct);

        atLimit.IsSuccess.Should().BeTrue();
        atLimit.Value!.Content.Length.Should().Be(Max);
        over.Error!.Kind.Should().Be(ErrorKind.PayloadTooLarge);
    }

    [Fact]
    public async Task AC_A_32_AClaimedLengthBelowTheLimit_ButMoreBytesInTheStream_Is413()
    {
        var result = await Service.ValidateAsync(File(Jpeg(Max + 10), "image/jpeg", length: 1000), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.PayloadTooLarge);
    }

    [Fact]
    public async Task AC_A_32_ASixMegabyteFileOfAnyType_Is413_BeforeTheTypeIsChecked()
    {
        var result = await Service.ValidateAsync(File(new byte[Max + 1], "image/svg+xml"), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.PayloadTooLarge);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/gif")]
    [InlineData("image/heic")]
    [InlineData("image/avif")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AC_A_31_ADeclaredTypeThatIsNotAccepted_Is415_EvenWithAValidSignature(string? declared)
    {
        var result = await Service.ValidateAsync(File(Jpeg(), declared), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.UnsupportedMediaType);
        result.Error.Code.Should().Be("unsupported_image_type");
    }

    [Theory]
    [InlineData("image/jpeg", "png")]
    [InlineData("image/png", "jpeg")]
    [InlineData("image/webp", "jpeg")]
    [InlineData("image/jpeg", "svg")]
    [InlineData("image/jpeg", "gif")]
    public async Task AC_A_31_ASignatureThatDoesNotMatchTheDeclaredType_Is415(string declared, string actual)
    {
        byte[] bytes = actual switch
        {
            "png" => Png(),
            "jpeg" => Jpeg(),
            "svg" => System.Text.Encoding.ASCII.GetBytes("<svg/>"),
            _ => System.Text.Encoding.ASCII.GetBytes("GIF89a"),
        };

        var result = await Service.ValidateAsync(File(bytes, declared), Ct);

        result.Error!.Kind.Should().Be(ErrorKind.UnsupportedMediaType);
        result.Error.Code.Should().Be("unsupported_image_type");
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("IMAGE/JPEG", "jpg")]
    [InlineData("image/jpeg; charset=binary", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    public async Task BR_A_23_AMatchingDeclaredTypeAndSignature_IsAccepted_WithTheDetectedTypeAndExtension(string declared, string extension)
    {
        byte[] bytes = extension switch { "png" => Png(), "webp" => WebP(), _ => Jpeg() };

        var result = await Service.ValidateAsync(File(bytes, declared), Ct);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Extension.Should().Be(extension);
        result.Value.ContentType.Should().StartWith("image/");
    }

    [Fact]
    public async Task BR_A_26_TheOriginalFileName_IsTruncatedTo255_AndLosesControlCharacters()
    {
        var result = await Service.ValidateAsync(File(Jpeg(), "image/jpeg", new string('a', 300) + "\r\n.jpg"), Ct);

        result.Value!.OriginalFileName!.Length.Should().Be(255);
        result.Value.OriginalFileName.Should().NotContain("\r").And.NotContain("\n");
    }

    // ---- storing: BR-A-26, AC-A-35, AC-A-36 -----------------------------------------------------------------------

    [Theory]
    [InlineData(ImageOwner.Product, "products/7/")]
    [InlineData(ImageOwner.Brand, "brands/7/")]
    [InlineData(ImageOwner.Category, "categories/7/")]
    [InlineData(ImageOwner.Banner, "banners/7/")]
    public void BR_A_26_TheBlobPath_IsServerGenerated_PerOwner_AndAcceptedByTheUrlBuilder(ImageOwner owner, string prefix)
    {
        var path = ImageUploadService.BuildPath(owner, 7, "webp");

        path.Should().StartWith(prefix).And.EndWith(".webp");
        path[prefix.Length..^".webp".Length].Should().MatchRegex("^[0-9a-f]{32}$");
        ImagePublicUrl.Build("https://images.test/catalog-images", path).Should().StartWith("https://images.test/catalog-images/");
    }

    [Fact]
    public async Task AC_A_35_TheClientFileName_NeverReachesTheBlobPath()
    {
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg", "../../evil.jpg"), Ct);

        var stored = await Service.StoreAsync(ImageOwner.Product, 3, validated.Value!, path => Task.FromResult(Result<string>.Ok(path)), null, Ct);

        stored.Value.Should().NotContain("evil").And.NotContain("..");
        validated.Value!.OriginalFileName.Should().Be("../../evil.jpg", "the name is data only");
    }

    [Fact]
    public async Task AC_A_36_WhenTheDatabaseStepReturnsAnError_TheNewBlobIsDeleted()
    {
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg"), Ct);

        var stored = await Service.StoreAsync(
            ImageOwner.Product, 3, validated.Value!,
            _ => Task.FromResult(Result<string>.Conflict("duplicate_slug")), null, Ct);

        stored.Error!.Code.Should().Be("duplicate_slug");
        _storage.Paths.Should().BeEmpty("the compensating delete removed the blob");
        _storage.Uploaded.Should().HaveCount(1, "the blob was written first");
    }

    [Fact]
    public async Task AC_A_36_WhenTheDatabaseStepThrows_TheNewBlobIsDeleted_AndTheExceptionPropagates()
    {
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg"), Ct);

        var act = () => Service.StoreAsync<string>(
            ImageOwner.Product, 3, validated.Value!, _ => throw new InvalidOperationException("db down"), null, Ct);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("db down");
        _storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task BR_A_25_AReplacedBlob_IsDeletedAfterTheDatabaseStepSucceeded_AndKeptWhenItFails()
    {
        _storage.Seed("brands/1/old.png");
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg"), Ct);

        var failed = await Service.StoreAsync(
            ImageOwner.Brand, 1, validated.Value!, _ => Task.FromResult(Result<string>.Conflict("x")), "brands/1/old.png", Ct);
        failed.IsSuccess.Should().BeFalse();
        _storage.Paths.Should().ContainSingle().Which.Should().Be("brands/1/old.png");

        var ok = await Service.StoreAsync(
            ImageOwner.Brand, 1, validated.Value!, p => Task.FromResult(Result<string>.Ok(p)), "brands/1/old.png", Ct);

        ok.IsSuccess.Should().BeTrue();
        _storage.Paths.Should().ContainSingle().Which.Should().Be(ok.Value);
    }

    [Fact]
    public async Task BR_A_25_AFailingDeleteOfTheReplacedBlob_IsSwallowed()
    {
        _storage.Seed("brands/1/old.png");
        _storage.FailDeletes = true;
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg"), Ct);

        var ok = await Service.StoreAsync(
            ImageOwner.Brand, 1, validated.Value!, p => Task.FromResult(Result<string>.Ok(p)), "brands/1/old.png", Ct);

        ok.IsSuccess.Should().BeTrue("the row is already committed; an orphan blob is only logged");
    }

    [Fact]
    public async Task BR_A_26_TheBlobIsUploadedWithTheDetectedContentType_AndTheStorageNeverOverwrites()
    {
        var validated = await Service.ValidateAsync(File(Png(), "image/png"), Ct);

        await Service.StoreAsync(ImageOwner.Product, 3, validated.Value!, p => Task.FromResult(Result<string>.Ok(p)), null, Ct);

        _storage.Uploaded.Single().ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task EC_A_WhenTheStorageIsUnavailable_TheServiceReturnsStorageUnavailable_NeverPersists_AndStoresNothing()
    {
        _storage.FailUploads = true;
        var validated = await Service.ValidateAsync(File(Jpeg(), "image/jpeg"), Ct);
        var persisted = false;

        var stored = await Service.StoreAsync(
            ImageOwner.Product, 3, validated.Value!, p => { persisted = true; return Task.FromResult(Result<string>.Ok(p)); }, null, Ct);

        stored.IsSuccess.Should().BeFalse();
        stored.Error!.Kind.Should().Be(ErrorKind.ServiceUnavailable);
        stored.Error.Code.Should().Be("storage_unavailable");
        persisted.Should().BeFalse("the database step must not run when the blob was not written");
        _storage.Paths.Should().BeEmpty();
    }

    [Fact]
    public void BR_A_26_TheStorageImplementation_WritesAnImmutableOneYearCacheControl() =>
        AzureBlobImageStorage.ImmutableCacheControl.Should().Be("public, max-age=31536000, immutable");

    private sealed class FakeStorage : IImageStorage
    {
        private readonly List<string> _paths = [];

        public List<(string Path, string ContentType)> Uploaded { get; } = [];

        public bool FailDeletes { get; set; }

        public bool FailUploads { get; set; }

        public IReadOnlyList<string> Paths => _paths;

        public void Seed(string path) => _paths.Add(path);

        public Task UploadAsync(string blobPath, Stream content, string contentType, CancellationToken cancellationToken)
        {
            if (FailUploads)
            {
                throw new ImageStorageUnavailableException("down", new IOException("inner"));
            }

            _paths.Add(blobPath);
            Uploaded.Add((blobPath, contentType));
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string blobPath, CancellationToken cancellationToken)
        {
            if (FailDeletes)
            {
                throw new IOException("down");
            }

            _paths.Remove(blobPath);
            return Task.CompletedTask;
        }

        public string GetPublicUrl(string blobPath) => "https://images.test/" + blobPath;
    }
}
