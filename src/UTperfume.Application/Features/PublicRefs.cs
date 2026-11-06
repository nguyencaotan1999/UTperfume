namespace UTperfume.Application.Features;

/// <summary>Shared public reference shapes (architecture section 6.3). None carries an id, blob path or quantity.</summary>
public sealed record BrandRef(string Slug, string Name);

public sealed record CategoryRef(string Slug, string Name);

public sealed record ConcentrationRef(string Code, string Name);

/// <summary><c>Url</c> is built from the blob path and the configured public base URL.</summary>
public sealed record ImageDto(string Url, string? AltText);
