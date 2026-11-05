using Microsoft.Extensions.Options;
using UTperfume.IntegrationTests.Infrastructure;

namespace UTperfume.IntegrationTests.Api;

[Collection(DatabaseCollection.Name)]
public sealed class StartupValidationTests(TestDatabase database)
{
    private static void AssertStartFails(ApiFactory factory, string expectedMessagePart)
    {
        using (factory)
        {
            var act = () => factory.CreateClient();

            act.Should().Throw<OptionsValidationException>().WithMessage($"*{expectedMessagePart}*");
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Start_fails_when_low_stock_threshold_is_below_one(string value)
    {
        var factory = new ApiFactory(database, new Dictionary<string, string?> { ["Catalog:LowStockThreshold"] = value });

        AssertStartFails(factory, "LowStockThreshold");
    }

    [Fact]
    public void Start_fails_when_new_badge_days_is_out_of_range()
    {
        var factory = new ApiFactory(database, new Dictionary<string, string?> { ["Catalog:NewBadgeDays"] = "0" });

        AssertStartFails(factory, "NewBadgeDays");
    }

    [Fact]
    public void Start_fails_when_storage_has_neither_service_uri_nor_connection_string()
    {
        var factory = new ApiFactory(database, new Dictionary<string, string?> { ["Storage:Images:ConnectionString"] = "" });

        AssertStartFails(factory, "ServiceUri or ConnectionString");
    }

    [Fact]
    public void Start_fails_when_storage_public_base_url_is_missing()
    {
        var factory = new ApiFactory(database, new Dictionary<string, string?> { ["Storage:Images:PublicBaseUrl"] = "" });

        AssertStartFails(factory, "PublicBaseUrl");
    }

    [Fact]
    public async Task Development_environment_starts_when_azurite_is_not_running()
    {
        // Development loads appsettings.Development.json (Azurite). Storage is lazy, so the app must start
        // and serve requests whether or not Azurite is reachable.
        using var factory = new ApiFactory(database, environment: "Development");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
