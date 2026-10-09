using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Owns authenticated offline catalog reconciliation inside an unpublished native source transaction.</summary>
public static class ClusterRestoreCatalogReconciliation
{
    private const int SingleOwnerCount = 1;
    private const string InvalidVector = "The original local cut is absent or inconsistent in the verified restore vector.";
    private const string MarkerSpace = "cluster-restore-marker";
    private const string MarkerVersion = "v1";
    private const string MarkerTooLarge = "The complete restore marker exceeds its native transaction payload limit.";

    /// <summary>Verifies the genuine original cut, then stages only explicit new-generation catalog metadata.</summary>
    /// <param name="transaction">Actual unpublished original native transaction owned by catalog restore.</param>
    /// <param name="identity">Actual verified original store identity.</param>
    /// <param name="position">Actual recovered original position before reconciliation.</param>
    /// <param name="metadata">Bounded original archive metadata.</param>
    /// <param name="secret">Separately configured persisted administrator credential.</param>
    /// <param name="cuts">Complete coordinator-verified original archive vector.</param>
    /// <param name="mappings">Complete explicit fresh target configuration.</param>
    /// <param name="limits">Original centrally validated limits owner.</param>
    /// <param name="clock">Actual owning operator clock.</param>
    /// <param name="work">Original bounded downward work, never renewed by this method.</param>
    /// <returns>The exact original local cut, not restored request authority.</returns>
    public static ClusterBackupOwnerCut Reconcile(IAtomicTransaction transaction, StoreIdentity identity,
        long position, ReadOnlyMemory<byte> metadata, string secret, ImmutableArray<ClusterBackupOwnerCut> cuts,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, IOptions<DatabaseLimits> limits,
        TimeProvider clock, ReadExecutionBudget work)
    {
        var original = ClusterBackupNativeCutVerification.Require(transaction, identity, position, metadata,
            secret, limits, clock, work);
        ClusterRestoreMappingValidation.Require(original.CaptureId, cuts, mappings);
        var claimed = cuts.SingleOrDefault(cut => cut.Owner.PhysicalShardId == original.Owner.PhysicalShardId);
        if (claimed is null || !ClusterBackupMetadataEquality.OwnerCut(claimed, original))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidVector); }
        var marker = NativeSerialization.Serialize(new ClusterRestoreMarker(ClusterRestoreMarker.CurrentVersion,
            original, mappings));
        if (marker.Length > limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, MarkerTooLarge); }
        var selected = mappings.Single(mapping => mapping.Source.PhysicalShardId == original.Owner.PhysicalShardId);
        work.Check();
        StageCatalog(transaction, original, mappings, selected.Target);
        StagePlacements(transaction, original, mappings, work);
        transaction.Put(KeyCodec.Encode(MarkerSpace, MarkerVersion), marker);
        work.Check();
        transaction.ValidateCommit();
        return original;
    }

    /// <summary>Stages reconciliation and its genuine native completion row in the SAME transaction.</summary>
    /// <param name="transaction">Original actual unpublished source transaction.</param>
    /// <param name="identity">Independently verified original identity.</param>
    /// <param name="position">Actual original source position.</param>
    /// <param name="commitPosition">Actual next native position provided by Store.Commit.</param>
    /// <param name="metadata">Original bounded archive envelope payload.</param>
    /// <param name="secret">Separately supplied current persisted administrator credential.</param>
    /// <param name="cuts">Exact original complete vector.</param>
    /// <param name="mappings">Explicit original target mapping.</param>
    /// <param name="context">Immutable admitted operation slot.</param>
    /// <param name="limits">Original centrally validated limits owner.</param>
    /// <param name="clock">Actual operator clock.</param>
    /// <param name="work">Original downward bounded work.</param>
    /// <returns>The genuine original cut retained in both same-transaction records.</returns>
    public static ClusterBackupOwnerCut ReconcileSlot(IAtomicTransaction transaction, StoreIdentity identity,
        long position, long commitPosition, ReadOnlyMemory<byte> metadata, string secret,
        ImmutableArray<ClusterBackupOwnerCut> cuts, ImmutableArray<ClusterRestoreOwnerMapping> mappings,
        ClusterRestoreSlotContext context, IOptions<DatabaseLimits> limits, TimeProvider clock, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(work);
        ClusterRestoreSlotValidation.RequireSource(context, identity, position, mappings);
        if (commitPosition <= position)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidVector); }
        var original = Reconcile(transaction, identity, position, metadata, secret, cuts, mappings, limits, clock, work);
        var marker = NativeSerialization.Serialize(new ClusterRestoreMarker(ClusterRestoreMarker.CurrentVersion,
            original, mappings, context));
        if (marker.Length > limits.Value.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, MarkerTooLarge); }
        transaction.Put(KeyCodec.Encode(MarkerSpace, MarkerVersion), marker);
        // Historical rows are discarded only inside the verified, unchanged source-cut transaction.
        transaction.Delete(ClusterRestoreSlotKeys.AuthorityReset());
        transaction.PutRecord(ClusterRestoreSlotKeys.Reconciled(), new ClusterRestoreSlotCommit(
            ClusterRestoreSlotCommit.CurrentVersion, context, original, ClusterRestoreMappingDigest.Compute(mappings),
            commitPosition, ClusterRestoreSlotCommitKind.Reconciled));
        work.Check();
        transaction.ValidateCommit();
        return original;
    }

    private static void StageCatalog(IAtomicTransaction transaction, ClusterBackupOwnerCut original,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, PhysicalShardRecord target)
    {
        transaction.PutRecord(PhysicalShardCatalogRecordSerialization.CatalogKey(),
            new PhysicalShardCatalog(PhysicalShardCatalogProtocol.CurrentVersion,
                PhysicalShardCatalogProtocol.InitialRevision, target));
        if (mappings.Length > SingleOwnerCount)
        {
            var owners = mappings.Select(mapping => new RegisteredPhysicalOwnerV1(mapping.Target, mapping.Endpoints))
                .ToImmutableArray();
            var revision = original.RegisteredOwners?.Revision ?? PhysicalOwnerDirectoryProtocol.RevisionStep;
            var directory = new PhysicalOwnerDirectoryV1(PhysicalOwnerDirectoryProtocol.Version, revision, target, owners);
            transaction.Put(PhysicalOwnerDirectorySerialization.Key(), PhysicalOwnerDirectorySerialization.Encode(directory));
        }
    }

    private static void StagePlacements(IAtomicTransaction transaction, ClusterBackupOwnerCut original,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, ReadExecutionBudget work)
    {
        foreach (var partition in original.Partitions)
        {
            work.Check();
            if (partition.Placement.IsFallback)
            { continue; }
            var target = mappings.Single(mapping => mapping.Source.PhysicalShardId == partition.Placement.PhysicalShardId).Target;
            var row = new AtomicPartitionPlacementV1(AtomicPartitionPlacementProtocol.CurrentVersion,
                partition.Roster.Partition, target.PhysicalShardId, partition.Placement.Revision,
                target.Incarnation, target.VoterIds, target.PlacementEpoch);
            transaction.Put(AtomicPartitionPlacementSerialization.RowKey(row.Partition),
                AtomicPartitionPlacementSerialization.SerializeBounded(row));
        }
    }
}
