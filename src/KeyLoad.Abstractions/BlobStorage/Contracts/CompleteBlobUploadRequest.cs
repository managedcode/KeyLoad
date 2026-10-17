namespace KeyLoad;

/// <summary>Atomically publishes a fully accepted upload through head revision CAS.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="ExpectedIntegrityHash">The expected final sha256-chain-v1 value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CompleteBlobUploadRequest)]
public sealed record CompleteBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] string ExpectedIntegrityHash);
