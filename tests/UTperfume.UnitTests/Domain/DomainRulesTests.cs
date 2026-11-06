using UTperfume.Application.Common;
using UTperfume.Application.Domain;

namespace UTperfume.UnitTests.Domain;

public class SlugTests
{
    [Theory]
    [InlineData("Dior Sauvage", "dior-sauvage")]
    [InlineData("Đêm Hà Nội", "dem-ha-noi")]
    [InlineData("đ", "d")]
    [InlineData("Đà Lạt", "da-lat")]
    [InlineData("Crème Brûlée", "creme-brulee")]
    [InlineData("Eau Fraîche", "eau-fraiche")]
    [InlineData("Café", "cafe")]
    [InlineData("  Chanel   N°5  ", "chanel-n-5")]
    [InlineData("A--B__C", "a-b-c")]
    [InlineData("--Leading and trailing--", "leading-and-trailing")]
    [InlineData("Straße", "strasse")]
    [InlineData("Tom Ford: Oud Wood (EDP)", "tom-ford-oud-wood-edp")]
    public void Generate_transliterates_and_normalises(string name, string expected)
    {
        Slug.Generate(name).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData("日本語")]
    public void Generate_returns_empty_when_nothing_usable(string? name)
    {
        Slug.Generate(name).Should().BeEmpty();
    }

    [Fact]
    public void Generate_output_is_always_valid_and_capped_at_200()
    {
        var slug = Slug.Generate(string.Join(' ', Enumerable.Repeat("perfume", 60)));

        slug.Length.Should().BeLessThanOrEqualTo(Slug.MaxLength);
        slug.Should().NotEndWith("-");
        Slug.IsValid(slug).Should().BeTrue();
    }

    [Theory]
    [InlineData("a", true)]
    [InlineData("dior-sauvage-edp", true)]
    [InlineData("n5", true)]
    [InlineData("", false)]
    [InlineData("-a", false)]
    [InlineData("a-", false)]
    [InlineData("a--b", false)]
    [InlineData("A", false)]
    [InlineData("a b", false)]
    [InlineData("è", false)]
    [InlineData("a_b", false)]
    [InlineData("a\n", false)]
    public void IsValid_follows_the_pattern(string slug, bool expected)
    {
        Slug.IsValid(slug).Should().Be(expected);
    }

    [Fact]
    public void IsValid_enforces_length_boundary()
    {
        Slug.IsValid(new string('a', 200)).Should().BeTrue();
        Slug.IsValid(new string('a', 201)).Should().BeFalse();
        Slug.IsValid(null).Should().BeFalse();
    }
}

public class AvailabilityTests
{
    [Theory]
    [InlineData(5, 0, AvailabilityStatus.OutOfStock)]
    [InlineData(5, 1, AvailabilityStatus.LowStock)]
    [InlineData(5, 5, AvailabilityStatus.LowStock)]
    [InlineData(5, 6, AvailabilityStatus.InStock)]
    [InlineData(1, 0, AvailabilityStatus.OutOfStock)]
    [InlineData(1, 1, AvailabilityStatus.LowStock)]
    [InlineData(1, 2, AvailabilityStatus.InStock)]
    [InlineData(10, 10, AvailabilityStatus.LowStock)]
    [InlineData(10, 11, AvailabilityStatus.InStock)]
    [InlineData(5, int.MaxValue, AvailabilityStatus.InStock)]
    public void From_maps_quantity_at_the_boundaries(int threshold, int quantity, AvailabilityStatus expected)
    {
        Availability.From(quantity, threshold).Should().Be(expected);
    }

    [Fact]
    public void From_uses_the_configured_default_threshold()
    {
        var threshold = new CatalogOptions().LowStockThreshold;

        Availability.From(0, threshold).Should().Be(AvailabilityStatus.OutOfStock);
        Availability.From(1, threshold).Should().Be(AvailabilityStatus.LowStock);
        Availability.From(threshold, threshold).Should().Be(AvailabilityStatus.LowStock);
        Availability.From(threshold + 1, threshold).Should().Be(AvailabilityStatus.InStock);
    }

    [Fact]
    public void From_rejects_negative_quantity_and_threshold_below_one()
    {
        var negative = () => Availability.From(-1, 5);
        var zeroThreshold = () => Availability.From(1, 0);

        negative.Should().Throw<ArgumentOutOfRangeException>();
        zeroThreshold.Should().Throw<ArgumentOutOfRangeException>();
    }
}

public class MoneyTests
{
    [Theory]
    [InlineData(1_000_000, 1_250_000, 20)]
    [InlineData(999, 1000, 0)]
    [InlineData(1, 3, 66)]
    [InlineData(2, 3, 33)]
    [InlineData(0, 1000, 100)]
    [InlineData(1_250_000, 1_250_001, 0)]
    [InlineData(5_000_000, 8_900_000, 43)]
    public void DiscountPercent_floors_and_never_overstates(long price, long original, int expected)
    {
        Money.DiscountPercent(price, original).Should().Be(expected);
    }

    [Theory]
    [InlineData(100, null)]
    [InlineData(100, 100L)]
    [InlineData(100, 50L)]
    [InlineData(100, 0L)]
    public void DiscountPercent_is_null_without_a_valid_compare_at_price(long price, long? original)
    {
        Money.DiscountPercent(price, original).Should().BeNull();
    }

    [Fact]
    public void DiscountPercent_does_not_overflow_for_large_values()
    {
        Money.DiscountPercent(0, long.MaxValue).Should().Be(100);
    }

    [Theory]
    [InlineData(100, null, true)]
    [InlineData(100, 101L, true)]
    [InlineData(100, 100L, false)]
    [InlineData(100, 99L, false)]
    [InlineData(0, 1L, true)]
    [InlineData(0, 0L, false)]
    public void OriginalPrice_must_be_strictly_greater_than_price(long price, long? original, bool expected)
    {
        Money.IsValidOriginalPrice(price, original).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1_250_000, true)]
    [InlineData(-1, false)]
    public void Price_must_not_be_negative(long price, bool expected)
    {
        Money.IsValidPrice(price).Should().Be(expected);
    }

    [Fact]
    public void Currency_is_vnd()
    {
        Money.Currency.Should().Be("VND");
    }
}

public class VariantRulesTests
{
    [Theory]
    [InlineData(" edp-100 ", "EDP-100")]
    [InlineData("abc", "ABC")]
    public void NormalizeSku_trims_and_uppercases(string input, string expected)
    {
        VariantRules.NormalizeSku(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("0", true)]
    [InlineData("SAU-EDP.100_ML", true)]
    [InlineData("", false)]
    [InlineData("-A", false)]
    [InlineData("a", false)]
    [InlineData("A B", false)]
    [InlineData("A/B", false)]
    [InlineData("A\n", false)]
    public void IsValidSku_follows_the_pattern(string sku, bool expected)
    {
        VariantRules.IsValidSku(sku).Should().Be(expected);
    }

    [Fact]
    public void IsValidSku_enforces_64_characters()
    {
        VariantRules.IsValidSku(new string('A', 64)).Should().BeTrue();
        VariantRules.IsValidSku(new string('A', 65)).Should().BeFalse();
        VariantRules.IsValidSku(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("0.1", true)]
    [InlineData("30", true)]
    [InlineData("99.5", true)]
    [InlineData("5000", true)]
    [InlineData("5000.0", true)]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    [InlineData("5000.1", false)]
    [InlineData("50.25", false)]
    [InlineData("0.05", false)]
    public void IsValidVolume_boundaries(string volume, bool expected)
    {
        VariantRules.IsValidVolume(decimal.Parse(volume, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);
    }
}
