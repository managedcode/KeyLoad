using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class GlobalBranchLayoutRf3Setup
{
    internal static async Task<GlobalBranchLayoutRf3State> RunAsync(TwoRf3MembershipWave wave,
        DistributedSearchRf3Callers callers, CancellationToken token)
    {
        await PhysicalOwnerRegistrationRf3Observation.WaitAsync(wave.Application, token);
        var source = callers.Sdk(TwoRf3MembershipProtocol.Node1, wave.Profile.AdminKey);
        var target = callers.Sdk(TwoRf3MembershipProtocol.Node4, wave.Profile.AdminKey);
        var seed = await DistributedSearchRf3Seed.CreateAsync(wave, source, target, token);
        await DistributedSearchRf3Seed.ConfigureAsync(target, seed.Original.Destination.Principal, 3L, token);
        var original = await McpCallerAssertions.SdkSuccessAsync(await source.ReadAtomicPartitionPlacementAsync(
            new(GlobalBranchLayoutRf3State.Version, RemotePartitionQueryRf3Seed.Local), token));
        var directory = seed.Original.Destination.Directory;
        var receiving = directory.Owners.Single(entry => entry.Owner.PhysicalShardId != original.PhysicalShardId).Owner;
        var donor = directory.Owners.Single(entry => entry.Owner.PhysicalShardId == original.PhysicalShardId).Owner;
        await Assert.That(donor.PhysicalShardId).IsNotEqualTo(receiving.PhysicalShardId);
        await Assert.That(donor.Incarnation).IsNotEqualTo(receiving.Incarnation);
        var movement = new PartitionMoveRequest(Guid.NewGuid(), RemotePartitionQueryRf3Seed.Local,
            receiving.PhysicalShardId, original.Revision, PartitionMoveMode.Transfer);
        var state = new GlobalBranchLayoutRf3State(seed, original, movement, donor, receiving);
        await GlobalBranchLayoutRf3Receipts.InitialAsync(state);
        return state;
    }
}
