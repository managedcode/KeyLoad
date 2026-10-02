namespace KeyLoad;

/// <summary>Reserves bounded capacity and starts one scoped upload lifetime.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The scoped upload identity, separate from command identity.</param>
/// <param name="Length">The declared complete raw byte length.</param>
/// <param name="ExpectedRevision">The required existing head revision, zero for a new name.</param>
/// <param name="Access">Optional row access, otherwise inherited from the head.</param>
public sealed record BeginBlobUploadRequest(Guid CommandId, BlobRef Blob, Guid UploadId,
    long Length, long ExpectedRevision, RowAccess? Access = null);

/// <summary>Appends or identically repeats one bounded raw part.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="Ordinal">The zero-based ordered part position.</param>
/// <param name="Bytes">The raw part bytes serialized using strict base64.</param>
/// <param name="Sha256">The canonical lowercase SHA256 of these raw bytes.</param>
public sealed record WriteBlobPartRequest(Guid CommandId, BlobRef Blob, Guid UploadId,
    int Ordinal, ReadOnlyMemory<byte> Bytes, string Sha256);

/// <summary>Atomically publishes a fully accepted upload through head revision CAS.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
/// <param name="ExpectedIntegrityHash">The expected final sha256-chain-v1 value.</param>
public sealed record CompleteBlobUploadRequest(Guid CommandId, BlobRef Blob, Guid UploadId,
    string ExpectedIntegrityHash);

/// <summary>Aborts an upload and releases its unused reservation once.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
public sealed record AbortBlobUploadRequest(Guid CommandId, BlobRef Blob, Guid UploadId);

/// <summary>Publishes a revision tombstone while retaining bytes for bounded reclaim.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The required positive current head revision.</param>
public sealed record DeleteBlobRequest(Guid CommandId, BlobRef Blob, long ExpectedRevision);

/// <summary>Reclaims bounded parts of a selected expired, aborted or retired version.</summary>
/// <param name="CommandId">The stable command identity.</param>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The selected version identity.</param>
/// <param name="MaxParts">The inclusive deletion limit for this command.</param>
public sealed record ReclaimBlobRequest(Guid CommandId, BlobRef Blob, Guid UploadId,
    int MaxParts = BlobLimits.MaxReclaimParts);

/// <summary>Returns one canonical commit receipt and its bounded feature result.</summary>
/// <typeparam name="T">The bounded public blob result.</typeparam>
/// <param name="Receipt">The existing ordered commit identity, token and durability.</param>
/// <param name="Value">The upload, metadata or cleanup result.</param>
public sealed record BlobCommitResult<T>(CommitReceipt Receipt, T Value);
