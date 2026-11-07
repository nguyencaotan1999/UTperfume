using System.Buffers.Binary;

namespace UTperfume.Application.Features.Admin.Images;

/// <summary>The accepted image types, detected from the file signature (BR-A-23).</summary>
public sealed record DetectedImageType(string ContentType, string Extension);

/// <summary>
/// Reads the file signature (BR-A-23) and, when it is cheap, the pixel size from the header (BR-A-30). Nothing is
/// decoded or re-encoded, and a header that cannot be understood gives null dimensions, never an error.
/// </summary>
public static class ImageHeader
{
    public static readonly DetectedImageType Jpeg = new("image/jpeg", "jpg");
    public static readonly DetectedImageType Png = new("image/png", "png");
    public static readonly DetectedImageType WebP = new("image/webp", "webp");

    /// <summary>JPEG <c>FF D8 FF</c>, PNG <c>89 50 4E 47</c>, WebP <c>RIFF....WEBP</c>. Null for anything else.</summary>
    public static DetectedImageType? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return Jpeg;
        }

        if (data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return Png;
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP;
        }

        return null;
    }

    public static (int? Width, int? Height) ReadDimensions(DetectedImageType type, ReadOnlySpan<byte> data)
    {
        var (width, height) = type.ContentType switch
        {
            "image/png" => ReadPng(data),
            "image/jpeg" => ReadJpeg(data),
            "image/webp" => ReadWebP(data),
            _ => (0, 0),
        };

        return width > 0 && height > 0 ? (width, height) : (null, null);
    }

    private static (int, int) ReadPng(ReadOnlySpan<byte> data)
    {
        // 8-byte signature, then the IHDR chunk: length(4) "IHDR"(4) width(4) height(4).
        if (data.Length < 24 || !data[12..16].SequenceEqual("IHDR"u8))
        {
            return (0, 0);
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(data[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[20..24]);
        return width <= int.MaxValue && height <= int.MaxValue ? ((int)width, (int)height) : (0, 0);
    }

    private static (int, int) ReadJpeg(ReadOnlySpan<byte> data)
    {
        var i = 2;
        while (i + 4 <= data.Length)
        {
            if (data[i] != 0xFF)
            {
                return (0, 0);
            }

            var marker = data[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            // Standalone markers have no length: TEM, RSTn.
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD8)
            {
                i += 2;
                continue;
            }

            // Start-of-frame markers, except DHT (C4), JPG (C8) and DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > data.Length)
                {
                    return (0, 0);
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 5)..(i + 7)]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(data[(i + 7)..(i + 9)]);
                return (width, height);
            }

            if (marker is 0xDA or 0xD9)
            {
                return (0, 0);
            }

            i += 2 + BinaryPrimitives.ReadUInt16BigEndian(data[(i + 2)..(i + 4)]);
        }

        return (0, 0);
    }

    private static (int, int) ReadWebP(ReadOnlySpan<byte> data)
    {
        if (data.Length < 30)
        {
            return (0, 0);
        }

        var chunk = data[12..16];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Lossy: frame tag(3), start code 9D 01 2A, then 14-bit width and height.
            if (data[23] != 0x9D || data[24] != 0x01 || data[25] != 0x2A)
            {
                return (0, 0);
            }

            return (BinaryPrimitives.ReadUInt16LittleEndian(data[26..28]) & 0x3FFF,
                BinaryPrimitives.ReadUInt16LittleEndian(data[28..30]) & 0x3FFF);
        }

        if (chunk.SequenceEqual("VP8L"u8))
        {
            // Lossless: signature 2F, then 14-bit (width - 1) and 14-bit (height - 1).
            if (data[20] != 0x2F)
            {
                return (0, 0);
            }

            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[21..25]);
            return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
        }

        if (chunk.SequenceEqual("VP8X"u8))
        {
            // Extended: 24-bit (canvas width - 1) at 24 and 24-bit (canvas height - 1) at 27.
            return ((data[24] | (data[25] << 8) | (data[26] << 16)) + 1,
                (data[27] | (data[28] << 8) | (data[29] << 16)) + 1);
        }

        return (0, 0);
    }
}
