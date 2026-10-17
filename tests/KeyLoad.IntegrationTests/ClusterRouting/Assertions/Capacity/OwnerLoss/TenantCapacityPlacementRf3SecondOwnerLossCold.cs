using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3SecondOwnerLossCold
{
    private const int First = 0;
    private const int Second = 1;

    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] moves,
        TenantCapacityPlacementRf3OwnerLossHeld.State held)
    {
        var second = seeds.Single(seed => seed.Partition == moves[Second].Partition);
        await TenantCapacityPlacementRf3OwnerLossCold.RequireAsync(wave, second, moves[Second], held);
        var first = moves[First];
        var cuts = TwoRf3MembershipProtocol.Nodes.Select(node =>
            PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, first, Guid.Empty)).ToArray();
        await Assert.That(cuts.Length).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cuts);
        foreach (var cut in cuts)
        {
            await SqlRf3Protocol.EqualAsync(cut,
                PartitionMovementPublicParentRf3Cut.ReadStopped(wave, cut.Node, first, Guid.Empty));
        }
        await TenantCapacityPlacementRf3Cold.RequireFullColdOpenAsync(wave, first, cuts);
    }
}
