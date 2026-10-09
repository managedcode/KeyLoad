using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Each observation comes from actual original native files, recovered transactions and joined owners.</summary>
internal static class ClusterRestoreNativeSlots
{
    private const string DatabaseDirectory = "database";
    private const string StageSuffix = ".slot-stage";
    private const string Invalid = "The original native restore slot differs from its admitted operation.";

    internal static NativeCatalogRestoreSlotCompletion Continue(ClusterRestoreOperationRuntime runtime,
        ClusterRestoreSlot slot, string nodesRoot)
    {
        runtime.Work.Check();
        var source = runtime.Plan.SourceArchives.Single(value => value.OriginalCut.Owner.PhysicalShardId == slot.SourceOwnerId);
        var context = Context(runtime, slot, source);
        var destination = Path.Combine(ClusterRestorePathValidation.Relative(nodesRoot, slot.StageName), DatabaseDirectory);
        var stage = destination + StageSuffix;
        var secret = runtime.Configuration.Value.Credentials.Single(value => value.SourceOwnerId == slot.SourceOwnerId).Credential;
        return ZoneTreeStore.RestoreCatalogBackupSlot(source.CanonicalPath, destination, stage,
            runtime.Storage, runtime.Clock, context, runtime.Signing[slot.SourceOwnerId],
            (transaction, identity, position, metadata, admitted) =>
                _ = ClusterRestoreCatalogReconciliation.ReconcileSlot(transaction, identity, admitted.SourcePosition, position, metadata,
                    secret, runtime.Cuts, runtime.Plan.Mappings, admitted, runtime.Limits, runtime.Clock, runtime.Work),
            (view, identity, position, admitted) => Verify(runtime, source, secret, view, identity, position, admitted),
            runtime.Observer, runtime.Cancellation);
    }

    internal static NativeCatalogRestoreSlotCompletion RequireExisting(ClusterRestoreOperationRuntime runtime,
        ClusterRestoreSlot slot, string nodesRoot)
    {
        var directory = Path.Combine(ClusterRestorePathValidation.Relative(nodesRoot, slot.StageName), DatabaseDirectory);
        if (!Directory.Exists(directory))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        // A remembered completion never authorizes recreating an absent slot from its source archive.
        var source = runtime.Plan.SourceArchives.Single(value => value.OriginalCut.Owner.PhysicalShardId == slot.SourceOwnerId);
        var secret = runtime.Configuration.Value.Credentials.Single(value => value.SourceOwnerId == slot.SourceOwnerId).Credential;
        return ZoneTreeStore.ObserveCatalogBackupSlot(source.CanonicalPath, directory, runtime.Storage, runtime.Clock,
            Context(runtime, slot, source), runtime.Signing[slot.SourceOwnerId],
            (view, identity, position, admitted) => Verify(runtime, source, secret, view, identity, position, admitted),
            runtime.Cancellation);
    }

    private static ClusterRestoreSlotContext Context(ClusterRestoreOperationRuntime runtime,
        ClusterRestoreSlot slot, ClusterRestoreSource source)
        => new(ClusterRestoreSlotContext.CurrentVersion, runtime.Plan.OperationId, runtime.PlanDigest,
            slot.SlotOrdinal, source.EnvelopeDigest, source.OriginalCut.SourceNodeId,
            source.OriginalCut.Owner.Incarnation, source.OriginalCut.StorePosition, slot.TargetNodeId,
            slot.TargetIncarnation, slot.TargetSignerFingerprint);

    private static void Verify(ClusterRestoreOperationRuntime runtime, ClusterRestoreSource source, string secret,
        KeyLoad.Storage.IKeyValueView view, StoreIdentity identity, long position, ClusterRestoreSlotContext admitted)
        => _ = ZoneTreeStore.ReadVerifiedCatalogBackup(source.CanonicalPath, runtime.Storage,
            (originalView, originalIdentity, originalPosition, metadata) =>
            {
                if (originalPosition != admitted.SourcePosition)
                { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
                ClusterRestoreImageComparison.Require(originalView, originalIdentity, metadata,
                    view, identity, position, secret, runtime.Cuts, runtime.Plan.Mappings, admitted,
                    runtime.Limits, runtime.Clock, runtime.Work);
                return true;
            }, runtime.Cancellation);

    internal static ImmutableArray<NativeCatalogRestoreSlotCompletion> RequireAll(
        ClusterRestoreOperationRuntime runtime, string root)
    {
        var results = ImmutableArray.CreateBuilder<NativeCatalogRestoreSlotCompletion>(runtime.Plan.Slots.Length);
        foreach (var slot in runtime.Plan.Slots)
        { results.Add(RequireExisting(runtime, slot, root)); }
        return results.ToImmutable();
    }

    internal static void RequireSame(NativeCatalogRestoreSlotCompletion expected, NativeCatalogRestoreSlotCompletion actual)
    {
        if (expected != actual || expected.Context != actual.Context)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
    }
}
