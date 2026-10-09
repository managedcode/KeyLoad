using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Validates claimed owner metadata against the actual recovered original native cut.</summary>
public static class ClusterBackupNativeCutVerification
{
    private const string InvalidCut = "The catalog backup metadata does not match its original canonical native cut.";
    private const long NoAppliedIndex = 0;

    /// <summary>Authenticates the configured operator and compares the complete captured claims with actual native rows.</summary>
    /// <param name="view">Genuine recovered source view borrowed under its native read gate.</param>
    /// <param name="identity">Original identity verified from the native archive.</param>
    /// <param name="position">Actual recovered original native position.</param>
    /// <param name="metadata">Bounded verified native envelope payload.</param>
    /// <param name="secret">Separately configured operator credential.</param>
    /// <param name="limits">Original centrally validated limits owner.</param>
    /// <param name="clock">Actual operation clock used to evaluate persisted expiry.</param>
    /// <param name="work">Original downward work; verification never renews its lifetime.</param>
    /// <returns>The exact validated source metadata, which grants no restored request role.</returns>
    public static ClusterBackupOwnerCut Require(IKeyValueView view, StoreIdentity identity, long position,
        ReadOnlyMemory<byte> metadata, string secret, IOptions<DatabaseLimits> limits, TimeProvider clock,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        var admitted = work.CreateView(view);
        _ = ClusterRestoreOperatorValidation.RequireAdministrator(admitted, secret, clock.GetUtcNow());
        var cut = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(metadata.Span);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(admitted)
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidCut);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var appliedBytes = admitted.ReadOwnedValue(KeySpace.AppliedBytes);
        var applied = appliedBytes is null ? NoAppliedIndex : NativeSerialization.Deserialize<long>(appliedBytes);
        if (cut is null || cut.Version != ClusterBackupOwnerCut.CurrentVersion || cut.CaptureId == Guid.Empty
            || cut.SourceNodeId != identity.NodeId || cut.StorePosition != position || cut.AppliedIndex != applied
            || applied <= NoAppliedIndex || cut.Owner is null || cut.Owner.VoterIds.IsDefault
            || cut.Owner.Incarnation != identity.Incarnation || cut.Catalog is null
            || cut.Catalog.Version != catalog.Version || cut.Catalog.Revision != catalog.Revision
            || !PhysicalOwnerEntryValidation.SameOwner(cut.Owner, catalog.DefaultShard)
            || cut.Catalog.DefaultShard is null || cut.Catalog.DefaultShard.VoterIds.IsDefault
            || !PhysicalOwnerEntryValidation.SameOwner(cut.Catalog.DefaultShard, catalog.DefaultShard)
            || cut.Directory != AtomicPartitionPlacementSerialization.ReadDirectory(admitted)
            || !ClusterBackupMetadataEquality.Registered(cut.RegisteredOwners,
                PhysicalOwnerDirectorySerialization.Read(admitted)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCut); }
        var entries = ClusterBackupRosterCapture.Read(admitted, identity.Incarnation, position, applied, limits.Value);
        var census = ClusterBackupCanonicalCensus.Require(admitted, entries, limits.Value, work.Cancellation);
        RequirePartitions(admitted, cut, entries, census, work);
        if (!string.Equals(cut.CanonicalDigest, census.Digest, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCut); }
        work.Check();
        return cut;
    }

    private static void RequirePartitions(IKeyValueView view, ClusterBackupOwnerCut cut,
        Dictionary<PartitionRef, ClusterBackupRosterCapture.CapturedEntry> entries,
        ClusterBackupCanonicalCensus.Census census, ReadExecutionBudget work)
    {
        if (cut.Partitions.IsDefault || cut.Partitions.Length != entries.Count)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidCut); }
        var seen = new HashSet<PartitionRef>();
        foreach (var partition in cut.Partitions)
        {
            work.Check();
            if (partition is null || partition.Roster is null || partition.Roster.Partition is null
                || partition.Placement is null
                || partition.Version != ClusterBackupOwnerCut.CurrentVersion || partition.AppliedIndex != cut.AppliedIndex
                || !seen.Add(partition.Roster.Partition)
                || !entries.TryGetValue(partition.Roster.Partition, out var actual)
                || !census.Partitions.TryGetValue(partition.Roster.Partition, out var scope)
                || partition.CanonicalRecordCount != scope.Count
                || !string.Equals(partition.CanonicalDigest, scope.Digest, StringComparison.Ordinal)
                || partition.Roster != actual.Entry || !string.Equals(partition.RosterDigest, actual.Digest, StringComparison.Ordinal)
                || !ClusterBackupMetadataEquality.Placement(partition.Placement,
                    DatabaseEngine.ReadClusterBackupPlacement(view, partition.Roster.Partition, cut.Owner)))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidCut); }
        }
    }
}
