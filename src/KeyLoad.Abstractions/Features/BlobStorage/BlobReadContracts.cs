using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests current authorized object metadata without binary payload.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
public sealed record BlobMetadataRequest(BlobRef Blob);

/// <summary>Requests authorized creator-scoped upload progress.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
public sealed record BlobUploadInfoRequest(BlobRef Blob, Guid UploadId);

/// <summary>Requests a bounded raw range at one required current object revision.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The positive revision required for this read cut.</param>
/// <param name="Offset">The zero-based raw byte offset, including an empty EOF range.</param>
/// <param name="Count">The exact bounded number of requested bytes.</param>
public sealed record BlobReadRequest(BlobRef Blob, long ExpectedRevision, long Offset, int Count);

/// <summary>Requests a bounded ordered listing of authorized live object metadata.</summary>
/// <param name="Partition">The complete atomic partition scope.</param>
/// <param name="Resource">The configured binary resource.</param>
/// <param name="Limit">The maximum returned visible metadata rows.</param>
/// <param name="AfterId">The optional exclusive last-visited object identifier.</param>
public sealed record BlobListRequest(PartitionRef Partition, string Resource,
    int Limit = BlobLimits.MaxListItems, string? AfterId = null);

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
public sealed record BlobUploadInfo(BlobRef Blob, Guid UploadId, long DeclaredLength,
    long ExpectedRevision, int NextOrdinal, long StoredBytes, DateTimeOffset ExpiresAt,
    BlobUploadStatus Status, string IntegrityHash);

/// <summary>Describes one authorized current head or positive-revision tombstone.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="Revision">The positive published head revision.</param>
/// <param name="VersionId">The current upload/version identity, null on a tombstone.</param>
/// <param name="Length">The published raw length, zero on a tombstone.</param>
/// <param name="PartCount">The published part count, zero on a tombstone.</param>
/// <param name="IntegrityHash">The final sha256-chain-v1 value, null on a tombstone.</param>
/// <param name="Access">The persisted row access authority.</param>
/// <param name="UpdatedAt">The trusted publication or deletion time.</param>
/// <param name="Deleted">Whether this metadata represents a revision tombstone.</param>
public sealed record BlobMetadata(BlobRef Blob, long Revision, Guid? VersionId, long Length,
    int PartCount, string? IntegrityHash, RowAccess Access, DateTimeOffset UpdatedAt, bool Deleted = false);

/// <summary>Returns exact requested bytes and metadata from one scoped read cut.</summary>
/// <param name="Metadata">The authorized required head revision.</param>
/// <param name="Offset">The requested raw starting offset.</param>
/// <param name="Bytes">The independent bounded raw range.</param>
public sealed record BlobReadResult(BlobMetadata Metadata, long Offset, ReadOnlyMemory<byte> Bytes);

/// <summary>Returns a bounded visible metadata page and an exclusive progress cursor.</summary>
/// <param name="Items">The authorized live metadata rows.</param>
/// <param name="NextAfterId">The last visited ID, or null after exhaustion.</param>
public sealed record BlobListPage(ImmutableArray<BlobMetadata> Items, string? NextAfterId);

/// <summary>Returns logical deletion progress without reclaiming a current version.</summary>
/// <param name="UploadId">The selected upload/version identity.</param>
/// <param name="DeletedParts">The parts logically deleted in this command.</param>
/// <param name="RemainingParts">The remaining accepted parts.</param>
/// <param name="ReleasedBytes">The unused reservation and deleted logical bytes released now.</param>
/// <param name="Complete">Whether this version has no remaining state or parts.</param>
public sealed record BlobReclaimResult(Guid UploadId, int DeletedParts, int RemainingParts,
    long ReleasedBytes, bool Complete);
