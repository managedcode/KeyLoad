using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests current authorized object metadata without binary payload.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobMetadataRequest)]
public sealed record BlobMetadataRequest([property: Orleans.Id(0)] BlobRef Blob);

/// <summary>Requests authorized creator-scoped upload progress.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="UploadId">The upload identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobUploadInfoRequest)]
public sealed record BlobUploadInfoRequest([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] Guid UploadId);

/// <summary>Requests a bounded raw range at one required current object revision.</summary>
/// <param name="Blob">The complete atomic object scope.</param>
/// <param name="ExpectedRevision">The positive revision required for this read cut.</param>
/// <param name="Offset">The zero-based raw byte offset, including an empty EOF range.</param>
/// <param name="Count">The exact bounded number of requested bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReadRequest)]
public sealed record BlobReadRequest([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] long ExpectedRevision, [property: Orleans.Id(2)] long Offset, [property: Orleans.Id(3)] int Count);

/// <summary>Requests a bounded ordered listing of authorized live object metadata.</summary>
/// <param name="Partition">The complete atomic partition scope.</param>
/// <param name="Resource">The configured binary resource.</param>
/// <param name="Limit">The maximum returned visible metadata rows.</param>
/// <param name="AfterId">The optional exclusive last-visited object identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobListRequest)]
public sealed record BlobListRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Resource,
    [property: Orleans.Id(2)] int Limit = BlobLimits.MaxListItems, [property: Orleans.Id(3)] string? AfterId = null);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobMetadata)]
public sealed record BlobMetadata([property: Orleans.Id(0)] BlobRef Blob, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] Guid? VersionId, [property: Orleans.Id(3)] long Length,
    [property: Orleans.Id(4)] int PartCount, [property: Orleans.Id(5)] string? IntegrityHash, [property: Orleans.Id(6)] RowAccess Access, [property: Orleans.Id(7)] DateTimeOffset UpdatedAt, [property: Orleans.Id(8)] bool Deleted = false);

/// <summary>Returns exact requested bytes and metadata from one scoped read cut.</summary>
/// <param name="Metadata">The authorized required head revision.</param>
/// <param name="Offset">The requested raw starting offset.</param>
/// <param name="Bytes">The independent bounded raw range.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReadResult)]
public sealed record BlobReadResult([property: Orleans.Id(0)] BlobMetadata Metadata, [property: Orleans.Id(1)] long Offset, [property: Orleans.Id(2)] ReadOnlyMemory<byte> Bytes);

/// <summary>Returns a bounded visible metadata page and an exclusive progress cursor.</summary>
/// <param name="Items">The authorized live metadata rows.</param>
/// <param name="NextAfterId">The last visited ID, or null after exhaustion.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobListPage)]
public sealed record BlobListPage([property: Orleans.Id(0)] ImmutableArray<BlobMetadata> Items, [property: Orleans.Id(1)] string? NextAfterId);

/// <summary>Returns logical deletion progress without reclaiming a current version.</summary>
/// <param name="UploadId">The selected upload/version identity.</param>
/// <param name="DeletedParts">The parts logically deleted in this command.</param>
/// <param name="RemainingParts">The remaining accepted parts.</param>
/// <param name="ReleasedBytes">The unused reservation and deleted logical bytes released now.</param>
/// <param name="Complete">Whether this version has no remaining state or parts.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BlobReclaimResult)]
public sealed record BlobReclaimResult([property: Orleans.Id(0)] Guid UploadId, [property: Orleans.Id(1)] int DeletedParts, [property: Orleans.Id(2)] int RemainingParts,
    [property: Orleans.Id(3)] long ReleasedBytes, [property: Orleans.Id(4)] bool Complete);
