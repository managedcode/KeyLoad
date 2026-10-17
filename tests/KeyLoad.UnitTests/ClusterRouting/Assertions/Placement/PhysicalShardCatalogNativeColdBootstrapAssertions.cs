using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalShardCatalogNativeColdBootstrapAssertions
{
    private const int FirstOwner = 0;
    private const int CatalogVersion = 1;
    private const long CatalogRevision = 1;
    private const long AbsentRevision = 0;

    internal static async Task<ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot>> VerifyAsync(
        ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        await Assert.That(owners.Nodes.Count).IsEqualTo(ControlledPartitionMovementNativeHostEndpoints.OwnerCount);
        var snapshots = await owners.CaptureAsync(token);
        for (var index = FirstOwner; index < owners.Nodes.Count; index++)
        {
            var node = owners.Nodes[index];
            var expectedOwner = index < ControlledPartitionMovementNativeHostEndpoints.GroupSize
                ? owners.Control.Owner : owners.Destination.Owner;
            await Assert.That(node.Silo.DatabaseReady).IsTrue();
            await Assert.That(node.Silo.CatalogReady).IsTrue();
            var persisted = node.Database.ReadPhysicalShardCatalog(PhysicalShardCatalogFixture.RootPrincipalId);
            var raw = node.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view));
            var expected = new PhysicalShardCatalog(CatalogVersion, CatalogRevision, expectedOwner);
            await EqualAsync(persisted, expected);
            await EqualAsync(raw, expected);
            var principal = node.Store.Read(view => node.Database.Principal(view,
                PhysicalShardCatalogFixture.RootPrincipalId, node.Clock.GetUtcNow()));
            await Assert.That(principal.Id).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
            await Assert.That(principal.ClusterAdministrator).IsTrue();
            var bootstrap = Bootstrap(snapshots[index], expectedOwner.PhysicalShardId);
            var operation = bootstrap.Operation ?? throw new InvalidOperationException("The native startup operation is absent.");
            await Assert.That(operation.Kind).IsEqualTo(OperationKind.BootstrapPhysicalShardCatalog);
            await Assert.That(operation.PrincipalId).IsEqualTo(PhysicalShardCatalogFixture.RootPrincipalId);
            await EqualAsync(NativeCommandPayload.Read<BootstrapPhysicalShardCatalogRequest>(operation),
                new BootstrapPhysicalShardCatalogRequest(CatalogVersion, AbsentRevision,
                    expectedOwner.PhysicalShardId, expectedOwner.Incarnation, expectedOwner.VoterIds));
            await Assert.That(bootstrap.Index <= snapshots[index].State.CommittedIndex).IsTrue();
            await Assert.That(node.Database.LastApplied >= bootstrap.Index).IsTrue();
        }
        return snapshots;
    }

    internal static async Task SameAuthorityAsync(ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> original,
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> current)
    {
        await Assert.That(current.Length).IsEqualTo(original.Length);
        for (var index = FirstOwner; index < original.Length; index++)
        {
            await EqualAsync(current[index].Identity, original[index].Identity);
            var before = original[index].Entries.Single(entry => entry.Operation?.Kind == OperationKind.BootstrapPhysicalShardCatalog);
            var shard = NativeCommandPayload.Read<BootstrapPhysicalShardCatalogRequest>(before.Operation!).PhysicalShardId;
            await EqualAsync(Bootstrap(current[index], shard), before);
        }
    }

    private static ReplicaEntry Bootstrap(ControlledPartitionMovementNativeProcessSnapshot snapshot, Guid shard)
        => snapshot.Entries.Single(entry => entry.Operation?.Id == PhysicalShardCatalogIdentity.CreateBootstrapCommandId(shard));

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
}
