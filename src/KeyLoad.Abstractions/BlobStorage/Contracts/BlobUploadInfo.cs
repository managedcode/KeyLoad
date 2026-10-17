namespace KeyLoad;

/// <summary>Returns immutable scoped upload progress without raw bytes.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="DeclaredLength">The complete declared raw byte length.</param>
/// <param name="ExpectedRevision">The head revision selected at begin.</param>
/// <param name="NextOrdinal">The next contiguous part ordinal.</param>
/// <param name="StoredBytes">The accepted raw bytes.</param>
/// <param name="ExpiresAt">The trusted evaluated upload expiry.</param>
/// <param name="Status">The persisted public upload lifecycle.</param>
/// <param name="IntegrityHash">The current sha256-chain-v1 value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobUploadInfo)]
public sealed record BlobUploadInfo([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] Guid UploadId, [property: Orleans.Id(2)] long DeclaredLength,
    [property: Orleans.Id(3)] long ExpectedRevision, [property: Orleans.Id(4)] int NextOrdinal, [property: Orleans.Id(5)] long StoredBytes, [property: Orleans.Id(6)] DateTimeOffset ExpiresAt,
    [property: Orleans.Id(7)] BlobUploadStatus Status, [property: Orleans.Id(8)] string IntegrityHash);
