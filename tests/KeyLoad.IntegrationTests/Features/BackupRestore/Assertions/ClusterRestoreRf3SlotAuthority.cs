using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Checks genuine stopped native rows against the fixture's original operation and source vector.</summary>
internal static class ClusterRestoreRf3SlotAuthority
{
    internal static async Task<ClusterRestoreSlotContext> RequireAsync(ZoneTreeStore store,
        ClusterRestoreMarker marker, ClusterBackupOwnerReceipt original, ClusterRestoreOwnerMapping mapping,
        int ordinal, Guid operationId, ImmutableArray<ClusterRestoreOwnerMapping> mappings, string expectedSignerFingerprint)
    {
        var actual = store.Read(view => (
            Reconciled: view.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.Reconciled()),
            Reset: view.GetRecord<ClusterRestoreSlotCommit>(ClusterRestoreSlotKeys.AuthorityReset())));
        var context = marker.SlotContext ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        var reconciled = actual.Reconciled ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        var reset = actual.Reset ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid);
        var expected = context with
        {
            Version = ClusterRestoreSlotContext.CurrentVersion,
            OperationId = operationId,
            SlotOrdinal = ordinal,
            SourceEnvelopeDigest = original.ManifestDigest,
            SourceNodeId = original.Cut.SourceNodeId,
            SourceIncarnation = original.Cut.Owner.Incarnation,
            SourcePosition = original.Cut.StorePosition,
            TargetNodeId = store.Identity.NodeId,
            TargetIncarnation = mapping.Target.Incarnation,
            TargetSignerFingerprint = expectedSignerFingerprint
        };
        await Assert.That(string.Equals(Convert.ToHexStringLower(SHA256.HashData(store.Identity.SigningKey.Span)),
            expectedSignerFingerprint, StringComparison.Ordinal)).IsTrue();
        await SqlRf3Protocol.EqualAsync(expected, context);
        await SqlRf3Protocol.EqualAsync(context, reconciled.Context);
        await SqlRf3Protocol.EqualAsync(context, reset.Context);
        await SqlRf3Protocol.EqualAsync(original.Cut, reconciled.OriginalCut);
        await SqlRf3Protocol.EqualAsync(original.Cut, reset.OriginalCut);
        await Assert.That(reconciled.Version).IsEqualTo(ClusterRestoreSlotCommit.CurrentVersion);
        await Assert.That(reset.Version).IsEqualTo(ClusterRestoreSlotCommit.CurrentVersion);
        await Assert.That(reconciled.Kind).IsEqualTo(ClusterRestoreSlotCommitKind.Reconciled);
        await Assert.That(reset.Kind).IsEqualTo(ClusterRestoreSlotCommitKind.AuthorityReset);
        await Assert.That(reconciled.NativeCommitPosition).IsGreaterThan(original.Cut.StorePosition);
        await Assert.That(reset.NativeCommitPosition).IsGreaterThan(reconciled.NativeCommitPosition);
        await Assert.That(reset.NativeCommitPosition).IsLessThanOrEqualTo(store.Position);
        var expectedMappingsDigest = ClusterRestoreMappingDigest.Compute(mappings);
        await Assert.That(reconciled.MappingsDigest).IsEqualTo(expectedMappingsDigest);
        await Assert.That(reset.MappingsDigest).IsEqualTo(expectedMappingsDigest);
        return context;
    }
}
