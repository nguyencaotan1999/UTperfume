using System.ComponentModel.DataAnnotations;
using UTperfume.Application.Common;

namespace UTperfume.UnitTests;

public class CatalogOptionsTests
{
    private static bool IsValid(CatalogOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);

    [Fact]
    public void Defaults_are_valid_and_match_the_architecture()
    {
        var options = new CatalogOptions();

        options.NewBadgeDays.Should().Be(30);
        options.LowStockThreshold.Should().Be(5);
        IsValid(options).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LowStockThreshold_must_be_at_least_one(int value)
    {
        IsValid(new CatalogOptions { LowStockThreshold = value }).Should().BeFalse();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(0, false)]
    [InlineData(366, false)]
    public void NewBadgeDays_boundaries(int value, bool expected)
    {
        IsValid(new CatalogOptions { NewBadgeDays = value }).Should().Be(expected);
    }
}
