using UTperfume.Infrastructure.Storage;

namespace UTperfume.UnitTests.Storage;

public class ImageStorageOptionsValidatorTests
{
    private static readonly ImageStorageOptionsValidator Validator = new();

    private static ImageStorageOptions Valid() => new()
    {
        ServiceUri = "https://account.blob.core.windows.net",
        Container = "catalog-images",
        PublicBaseUrl = "https://account.blob.core.windows.net/catalog-images",
    };

    [Fact]
    public void Accepts_service_uri_configuration()
    {
        Validator.Validate(null, Valid()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Accepts_connection_string_configuration()
    {
        var options = Valid();
        options.ServiceUri = null;
        options.ConnectionString = "UseDevelopmentStorage=true";

        Validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Rejects_when_neither_service_uri_nor_connection_string()
    {
        var options = Valid();
        options.ServiceUri = null;

        var result = Validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("ServiceUri or ConnectionString");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("ftp://host/catalog-images")]
    public void Rejects_invalid_public_base_url(string? value)
    {
        var options = Valid();
        options.PublicBaseUrl = value;

        Validator.Validate(null, options).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("Catalog-Images")]
    [InlineData("catalog--images")]
    [InlineData("-catalog")]
    [InlineData("catalog_images")]
    public void Rejects_invalid_container_name(string container)
    {
        var options = Valid();
        options.Container = container;

        Validator.Validate(null, options).Failed.Should().BeTrue();
    }

    [Fact]
    public void Rejects_relative_service_uri()
    {
        var options = Valid();
        options.ServiceUri = "account";

        Validator.Validate(null, options).Failed.Should().BeTrue();
    }
}
