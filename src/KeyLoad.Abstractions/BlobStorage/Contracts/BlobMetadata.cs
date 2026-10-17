namespace KeyLoad;

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
