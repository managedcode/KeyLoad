namespace KeyLoad;

/// <summary>Requests authorized creator-scoped upload progress.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobUploadInfoRequest)]
public sealed record BlobUploadInfoRequest([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] Guid UploadId);
