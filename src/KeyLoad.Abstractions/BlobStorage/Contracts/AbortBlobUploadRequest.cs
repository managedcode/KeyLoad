namespace KeyLoad;

/// <summary>Aborts an upload and releases its unused reservation once.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AbortBlobUploadRequest)]
public sealed record AbortBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId);
