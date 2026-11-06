using System.Reflection;
using UTperfume.Application.Features.Products;

namespace UTperfume.UnitTests.Domain;

/// <summary>BR-08 and BR-28: the public projection shapes expose status only, never a quantity.</summary>
public class PublicShapeTests
{
    public static TheoryData<Type> PublicRowTypes => new() { typeof(ProductSummaryRow), typeof(VariantAvailabilityRow) };

    [Theory]
    [MemberData(nameof(PublicRowTypes))]
    public void Public_rows_have_no_quantity_or_stock_member(Type type)
    {
        var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .ToList();

        members.Should().NotContain(n =>
            n.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            || n.Contains("StockCount", StringComparison.OrdinalIgnoreCase)
            || n.Equals("Stock", StringComparison.OrdinalIgnoreCase));

        // Also check the constructor parameters, which is where a positional record declares its members.
        type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name!)
            .Should().NotContain(n => n.Contains("Quantity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Variant_row_exposes_the_availability_status_instead()
    {
        typeof(VariantAvailabilityRow).GetProperty(nameof(VariantAvailabilityRow.Availability))!
            .PropertyType.Should().Be(typeof(UTperfume.Application.Domain.AvailabilityStatus));
    }

    [Fact]
    public void Summary_row_money_members_are_whole_dong_long()
    {
        typeof(ProductSummaryRow).GetProperty(nameof(ProductSummaryRow.FromPrice))!.PropertyType.Should().Be(typeof(long));
        typeof(ProductSummaryRow).GetProperty(nameof(ProductSummaryRow.FromOriginalPrice))!.PropertyType
            .Should().Be(typeof(long?));
    }
}
