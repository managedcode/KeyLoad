namespace KeyLoad;

/// <summary>Requests current authorized object metadata without binary payload.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobMetadataRequest)]
public sealed record BlobMetadataRequest([property: Orleans.Id(0)] BlobRef Blob);
