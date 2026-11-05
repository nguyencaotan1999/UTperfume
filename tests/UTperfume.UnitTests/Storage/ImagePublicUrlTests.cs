using UTperfume.Infrastructure.Storage;

namespace UTperfume.UnitTests.Storage;

public class ImagePublicUrlTests
{
    private const string Base = "http://127.0.0.1:10000/devstoreaccount1/catalog-images";

    [Fact]
    public void Build_joins_base_and_path()
    {
        ImagePublicUrl.Build(Base, "products/12/abc.jpg")
            .Should().Be("http://127.0.0.1:10000/devstoreaccount1/catalog-images/products/12/abc.jpg");
    }

    [Fact]
    public void Build_tolerates_trailing_slash_on_base()
    {
        ImagePublicUrl.Build(Base + "/", "brands/x.png")
            .Should().Be(Base + "/brands/x.png");
    }

    [Fact]
    public void Build_escapes_each_segment_but_keeps_separators()
    {
        ImagePublicUrl.Build(Base, "products/1/a b#c.jpg")
            .Should().Be(Base + "/products/1/a%20b%23c.jpg");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/products/1/a.jpg")]
    [InlineData("products//a.jpg")]
    [InlineData("products/../a.jpg")]
    [InlineData("./a.jpg")]
    [InlineData("products\\a.jpg")]
    public void Build_rejects_unsafe_paths(string path)
    {
        var act = () => ImagePublicUrl.Build(Base, path);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_rejects_missing_base()
    {
        var act = () => ImagePublicUrl.Build(" ", "a.jpg");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Storage_GetPublicUrl_uses_configured_base()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new ImageStorageOptions
        {
            ConnectionString = "UseDevelopmentStorage=true",
            PublicBaseUrl = Base,
        });
        var storage = new AzureBlobImageStorage(options);

        storage.GetPublicUrl("products/1/x.webp").Should().Be(Base + "/products/1/x.webp");
    }
}
