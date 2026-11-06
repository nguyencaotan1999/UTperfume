namespace UTperfume.Application.Domain;

/// <summary>Single implementation of the availability rule (BR-08, ADR-0005).</summary>
public static class Availability
{
    /// <summary>0 is out of stock, 1 to <paramref name="threshold"/> is low stock, above it is in stock.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Quantity is negative or threshold is below 1.</exception>
    public static AvailabilityStatus From(int quantity, int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);

        return quantity == 0 ? AvailabilityStatus.OutOfStock
            : quantity <= threshold ? AvailabilityStatus.LowStock
            : AvailabilityStatus.InStock;
    }
}
