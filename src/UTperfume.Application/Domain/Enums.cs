namespace UTperfume.Application.Domain;

/// <summary>Stored as tinyint (0002 section 3.4).</summary>
public enum ProductStatus : byte
{
    Draft = 0,
    Published = 1,
    Archived = 2,
}

/// <summary>Fragrance pyramid tier, stored as tinyint (0002 section 3.8).</summary>
public enum NoteType : byte
{
    Top = 1,
    Heart = 2,
    Base = 3,
}

/// <summary>Public stock status. The exact quantity is never exposed (BR-08).</summary>
public enum AvailabilityStatus
{
    OutOfStock = 0,
    LowStock = 1,
    InStock = 2,
}
