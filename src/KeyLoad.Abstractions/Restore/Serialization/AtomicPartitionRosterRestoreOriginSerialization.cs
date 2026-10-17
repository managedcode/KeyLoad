using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad;

/// <summary>Shares exact current native catalog keys and historical row identity checks.</summary>
public static class AtomicPartitionRosterRestoreOriginSerialization
{
    internal const string Alias = "keyload.backup.atomic-partition-roster-restore-origin.v1";
    /// <summary>The generated current-format origin version.</summary>
    public const int CurrentVersion = 1;
    /// <summary>The unchanged generated immutable roster entry version.</summary>
    public const int EntryVersion = 1;
    private const long NoAppliedIndex = 0;
    private const string KeySpace = "atomic-partition-catalog";
    private const string KeyVersion = "v1";
    private const string EntryKind = "partition";
    private const string OriginKind = "restore-origin";
    private const string IdentityKind = "restore-identity";

    /// <summary>Encodes the owning restore identity pair.</summary>
    public static byte[] IdentityKey() => KeyCodec.Encode(KeySpace, KeyVersion, IdentityKind);

    /// <summary>Encodes the bounded historical-origin scan prefix.</summary>
    public static byte[] OriginPrefix() => KeyCodec.Encode(KeySpace, KeyVersion, OriginKind);
    /// <summary>Encodes the immutable roster scan prefix.</summary>
    public static byte[] EntryPrefix() => KeyCodec.Encode(KeySpace, KeyVersion, EntryKind);
    /// <summary>Encodes the original four-field roster key without changing its bytes.</summary>
    /// <param name="partition">Complete logical partition scope.</param>
    public static byte[] EntryKey(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return KeyCodec.Encode(KeySpace, KeyVersion, EntryKind,
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey);
    }
    /// <summary>Encodes the historical origin key in its separate native family.</summary>
    /// <param name="partition">Complete logical partition scope.</param>
    public static byte[] OriginKey(PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(partition);
        return KeyCodec.Encode(KeySpace, KeyVersion, OriginKind,
            partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey);
    }

    /// <summary>Checks the actual source/new identity pair without granting live authority.</summary>
    /// <param name="identity">Actual native restore identity metadata.</param>
    /// <param name="currentIncarnation">Actual owning store incarnation.</param>
    public static bool MatchesIdentity(AtomicPartitionRosterRestoreIdentity identity, Guid currentIncarnation)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return identity.Version == CurrentVersion && identity.SourceIncarnation != Guid.Empty
            && identity.RestoredIncarnation == currentIncarnation && currentIncarnation != Guid.Empty
            && identity.SourceIncarnation != currentIncarnation;
    }

    /// <summary>Checks metadata identity only; it grants no live read or replica authority.</summary>
    /// <param name="origin">Actual native historical metadata.</param>
    /// <param name="partition">Actual scope decoded from the immutable roster row.</param>
    /// <param name="currentIncarnation">Actual owning store incarnation.</param>
    /// <param name="originalRow">Actual unchanged persisted roster bytes.</param>
    /// <param name="firstSeenAppliedIndex">Actual first observation decoded from that same row.</param>
    /// <param name="actualSourceIncarnation">Source from the owning native restore identity pair.</param>
    public static bool Matches(AtomicPartitionRosterRestoreOrigin origin, PartitionRef partition,
        Guid currentIncarnation, ReadOnlySpan<byte> originalRow, long firstSeenAppliedIndex, Guid actualSourceIncarnation)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return origin.Version == CurrentVersion && origin.Partition == partition
            && origin.SourceIncarnation != Guid.Empty && origin.SourceIncarnation == actualSourceIncarnation
            && origin.RestoredIncarnation == currentIncarnation
            && currentIncarnation != Guid.Empty && origin.SourceIncarnation != currentIncarnation
            && firstSeenAppliedIndex > NoAppliedIndex && origin.AppliedUpperBound >= firstSeenAppliedIndex && origin.EntryDigest is { Length: SHA256.HashSizeInBytes }
            && CryptographicOperations.FixedTimeEquals(origin.EntryDigest.Span, SHA256.HashData(originalRow));
    }
}
