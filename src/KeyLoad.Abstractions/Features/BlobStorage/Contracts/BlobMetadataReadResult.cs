namespace KeyLoad;

/// <summary>Contains the authorized current metadata, or successful absence.</summary>
/// <param name="Metadata">The complete projected metadata; null means no live blob exists.</param>
[Orleans.GenerateSerializer, Orleans.Alias(PublicReadResultAliases.BlobMetadata)]
public sealed record BlobMetadataReadResult([property: Orleans.Id(0)] BlobMetadata? Metadata);
