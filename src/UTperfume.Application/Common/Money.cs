namespace UTperfume.Application.Common;

/// <summary>VND helpers. Money is whole dong held in a <see cref="long"/> (decision D-5).</summary>
public static class Money
{
    public const string Currency = "VND";

    /// <summary>Upper bound accepted for price filters (architecture section 6.6).</summary>
    public const long MaxFilterPrice = 10_000_000_000;

    public static bool IsValidPrice(long price) => price >= 0;

    /// <summary>BR-07: the compare-at price is absent or strictly greater than the price.</summary>
    public static bool IsValidOriginalPrice(long price, long? originalPrice) =>
        originalPrice is null || originalPrice.Value > price;

    /// <summary>
    /// BR-07: <c>floor((original - price) * 100 / original)</c> in integer arithmetic, so a discount is never
    /// overstated. Null when there is no valid compare-at price.
    /// </summary>
    public static int? DiscountPercent(long price, long? originalPrice)
    {
        if (originalPrice is not { } original || original <= 0 || original <= price)
        {
            return null;
        }

        return (int)((Int128)(original - price) * 100 / original);
    }
}
