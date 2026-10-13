using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantDedicatedOwnerRf3Provisioning
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const long FirstDirectoryRevision = 1;
    private const long PackedDirectoryRevision = 2;
    private const int FirstOriginal = 0;

    internal static async Task<TenantDedicatedOwnerRf3State> CreateAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        return await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1,
            wave.Profile.AdminKey, async root =>
            {
                var sibling = await TenantPackingActivationRf3Provisioning.CreateAsync(root.Sdk,
                    "dedicated-packed-sibling-" + Guid.NewGuid().ToString("N"), token);
                await BindAsync(root, seed.Partition, InitialRevision, seed.Directory.ControlOwner.PhysicalShardId, token);
                await BindAsync(root, sibling.Partition, FirstDirectoryRevision, seed.Directory.ControlOwner.PhysicalShardId, token);
                var original = await TenantPackingActivationRf3Admission.ReadAsync(root, seed.Partition, token);
                var siblingPlacement = await TenantPackingActivationRf3Admission.ReadAsync(root, sibling.Partition, token);
                var owner = seed.Directory.ControlOwner;
                await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(Version, seed.Partition,
                    owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
                    PackedDirectoryRevision, FirstDirectoryRevision, false), original);
                await SqlRf3Protocol.EqualAsync(new AtomicPartitionPlacementResolution(Version, sibling.Partition,
                    owner.PhysicalShardId, owner.Incarnation, owner.VoterIds, owner.PlacementEpoch,
                    PackedDirectoryRevision, FirstDirectoryRevision, false), siblingPlacement);
                await AtomicPartitionPlacementRf3Assertions.SameOwnerAsync(original, siblingPlacement);
                await Assert.That(original.DirectoryRevision).IsEqualTo(PackedDirectoryRevision);
                await Assert.That(siblingPlacement.DirectoryRevision).IsEqualTo(PackedDirectoryRevision);
                await Assert.That(original.Partition.TenantId).IsNotEqualTo(sibling.Partition.TenantId);
                var command = TenantPackingActivationRf3Provisioning.Seed(sibling,
                    seed.Originals[FirstOriginal].Command.CommandId);
                var receipt = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application,
                    TwoRf3MembershipProtocol.Node1, sibling.Secret,
                    callers => TenantPackingActivationRf3Provisioning.CommitAsync(callers, command, token), token);
                var destination = seed.Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                    != seed.Directory.ControlOwner.PhysicalShardId).Owner;
                var move = new PartitionMoveRequest(Guid.NewGuid(), seed.Partition, destination.PhysicalShardId,
                    original.Revision, PartitionMoveMode.Transfer);
                var denied = new PartitionMoveRequest(Guid.NewGuid(), sibling.Partition, destination.PhysicalShardId,
                    siblingPlacement.Revision, PartitionMoveMode.Transfer);
                var physical = await NativeActivationMigrationRf3Ownership.CaptureAsync(wave, token);
                return new TenantDedicatedOwnerRf3State(sibling, command, receipt, original, siblingPlacement, move, denied, physical);
            }, token);
    }

    private static async Task BindAsync(RequestCqrsRf3Callers root, PartitionRef partition,
        long revision, Guid owner, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var request = new BindAtomicPartitionPlacementRequest(Version, revision, partition, owner);
        var bound = await root.Sdk.BindAtomicPartitionPlacementAsync(id, request, token);
        await Assert.That(bound.IsSuccess).IsTrue();
        await Assert.That(bound.Value).IsTrue();
        var replay = await root.Sdk.BindAtomicPartitionPlacementAsync(id, request, token);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Value).IsTrue();
    }
}
