namespace KeyLoad;

/// <summary>Reserves bounded capacity and starts one scoped upload lifetime.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The scoped upload identity, separate from command identity.</param>
/// <param name="Length">The declared complete raw byte length.</param>
/// <param name="ExpectedRevision">The required existing head revision, zero for a new name.</param>
/// <param name="Access">Optional row access, otherwise inherited from the head.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BeginBlobUploadRequest)]
public sealed record BeginBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] long Length, [property: Orleans.Id(4)] long ExpectedRevision, [property: Orleans.Id(5)] RowAccess? Access = null);

/// <summary>Appends or identically repeats one bounded raw part.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="Ordinal">The zero-based ordered part position.</param>
/// <param name="Bytes">The raw part bytes serialized using strict base64.</param>
/// <param name="Sha256">The canonical lowercase SHA256 of these raw bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.WriteBlobPartRequest)]
public sealed record WriteBlobPartRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] int Ordinal, [property: Orleans.Id(4)] ReadOnlyMemory<byte> Bytes, [property: Orleans.Id(5)] string Sha256);

/// <summary>Atomically publishes a fully accepted upload through head revision CAS.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="ExpectedIntegrityHash">The expected final sha256-chain-v1 value.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CompleteBlobUploadRequest)]
public sealed record CompleteBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] string ExpectedIntegrityHash);

/// <summary>Aborts an upload and releases its unused reservation once.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AbortBlobUploadRequest)]
public sealed record AbortBlobUploadRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId);

/// <summary>Publishes a revision tombstone while retaining bytes for bounded reclaim.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The required positive current head revision.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DeleteBlobRequest)]
public sealed record DeleteBlobRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] long ExpectedRevision);

/// <summary>Reclaims bounded parts of a selected expired, aborted or retired version.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The selected version identity.</param>
/// <param name="MaxParts">The inclusive deletion limit for this command.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReclaimBlobRequest)]
public sealed record ReclaimBlobRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] BlobRef Blob, [property: Orleans.Id(2)] Guid UploadId,
    [property: Orleans.Id(3)] int MaxParts = BlobLimits.MaxReclaimParts);

/// <summary>Returns one canonical commit receipt and its bounded feature result.</summary>
/// <typeparam name="T">The bounded public blob result.</typeparam>
/// <param name="Receipt">The existing ordered commit identity, token and durability.</param>
/// <param name="Value">The upload, metadata or cleanup result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobCommitResult)]
public sealed record BlobCommitResult<T>([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] T Value);
