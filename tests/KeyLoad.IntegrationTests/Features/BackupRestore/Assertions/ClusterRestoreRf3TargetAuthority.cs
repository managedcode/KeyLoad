using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

/// <summary>Reads genuine stopped native authority; only observed nonsecret identities escape.</summary>
internal static class ClusterRestoreRf3TargetAuthority
{
    private const string MarkerSpace = "cluster-restore-marker";
    private const string MarkerVersion = "v1";
    private const string CatalogSpace = "physical-shard-catalog";
    private const string CatalogVersion = "v1";
    private const string OwnersSpace = "physical-owner-directory";
    private const string OwnersVersion = "v1";
    private const string SystemSpace = "system";
    private const string DispatchKey = "dispatch-paused";
    private const int GroupMembers = 3;
    private const long InitialCatalogRevision = 1;

    internal static async Task<ClusterRestoreRf3OperatorNode> RequireAsync(ZoneTreeStore store,
        ImmutableArray<ClusterRestoreOwnerMapping> mappings, ImmutableArray<ClusterBackupOwnerReceipt> originals,
        ClusterBackupOwnerReceipt original, ClusterRestoreOwnerMapping mapping,
        ImmutableArray<ClusterRestoreRf3OperatorNode>.Builder previous, int index, bool dispatchPaused, Guid operationId, string expectedSignerFingerprint)
    {
        var actual = store.Read(view =>
        {
            var marker = NativeSerialization.Deserialize<ClusterRestoreMarker>(view.ReadOwnedValue(
                KeyCodec.Encode(MarkerSpace, MarkerVersion)) ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid));
            var catalog = NativeSerialization.Deserialize<PhysicalShardCatalog>(view.ReadOwnedValue(
                KeyCodec.Encode(CatalogSpace, CatalogVersion)) ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid));
            var owners = NativeSerialization.Deserialize<PhysicalOwnerDirectoryV1>(view.ReadOwnedValue(
                KeyCodec.Encode(OwnersSpace, OwnersVersion)) ?? throw new InvalidOperationException(ClusterRestoreRf3Protocol.Invalid));
            var paused = view.ReadOwnedValue(KeyCodec.Encode(SystemSpace, DispatchKey)) is { } row
                ? NativeSerialization.Deserialize<bool>(row) : store.Identity.DispatchPaused;
            return (Marker: marker, Catalog: catalog, Owners: owners, Paused: paused, Identity: store.Identity);
        });
        await SqlRf3Protocol.EqualAsync(new ClusterRestoreMarker(ClusterRestoreMarker.CurrentVersion,
            original.Cut, mappings, await ClusterRestoreRf3SlotAuthority.RequireAsync(store, actual.Marker, original, mapping, index, operationId, mappings, expectedSignerFingerprint).ConfigureAwait(false)), actual.Marker);
        await SqlRf3Protocol.EqualAsync(new PhysicalShardCatalog(ClusterRestoreRf3Protocol.CurrentVersion,
            InitialCatalogRevision, mapping.Target), actual.Catalog);
        var revision = original.Cut.RegisteredOwners?.Revision ?? InitialCatalogRevision;
        await SqlRf3Protocol.EqualAsync(new PhysicalOwnerDirectoryV1(ClusterRestoreRf3Protocol.CurrentVersion,
            revision, mapping.Target, [.. mappings.Select(item => new RegisteredPhysicalOwnerV1(
                        item.Target, item.Endpoints))]), actual.Owners);
        await Assert.That(actual.Identity.Incarnation).IsEqualTo(mapping.Target.Incarnation);
        foreach (var source in originals)
        { await Assert.That(actual.Identity.NodeId).IsNotEqualTo(source.Cut.SourceNodeId); }
        foreach (var prior in previous)
        { await Assert.That(actual.Identity.NodeId).IsNotEqualTo(prior.NodeId); }
        await Assert.That(actual.Identity.NodeId).IsNotEqualTo(Guid.Empty);
        await Assert.That(actual.Identity.DispatchPaused).IsTrue();
        await Assert.That(actual.Paused).IsEqualTo(dispatchPaused);
        return new(mapping.Source.PhysicalShardId, mapping.Target.PhysicalShardId,
            mapping.Target.VoterIds[index % GroupMembers], ClusterRestoreRf3Protocol.Nodes[index],
            actual.Identity.NodeId, actual.Identity.Incarnation, actual.Identity.DispatchPaused);
    }
}
