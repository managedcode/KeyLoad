using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementLifetimeProofRf3Cold
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, PartitionMovementLifetimeProofRf3Original[] probed,
        List<PartitionMovementLifetimeProofRf3Original> retained, CancellationToken token)
    {
        var root = wave.OwnedDataRoot;
        var before = await StoppedAsync(wave, seed, terminal, probed);
        await wave.RestartJoinedAsync(token);
        await seed.ReconnectAsync(wave, token);
        await Assert.That(wave.OwnedDataRoot).IsEqualTo(root);
        foreach (var original in probed)
        {
            await PartitionMovementLifetimeProofRf3Calls.ReplayAsync(seed, original, token);
        }
        await seed.VerifyAsync(token);
        var after = await StoppedAsync(wave, seed, terminal, probed);
        await SqlRf3Protocol.EqualAsync(before, after);
        await wave.RestartJoinedAsync(token);
        await seed.ReconnectAsync(wave, token);
        await Assert.That(wave.OwnedDataRoot).IsEqualTo(root);
        await FreshAsync(seed, terminal, retained, official: false, token);
        await FreshAsync(seed, terminal, retained, official: true, token);
        await seed.VerifyAsync(token);
        await StoppedAsync(wave, seed, terminal, retained.ToArray());
    }

    private static async Task FreshAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMoveResult terminal, List<PartitionMovementLifetimeProofRf3Original> retained,
        bool official, CancellationToken token)
    {
        var input = PartitionMovementLifetimeProofRf3Calls.Create(seed, terminal, official, cold: true);
        var original = PartitionMovementLifetimeProofRf3Calls.Start(seed, input, official, Guid.Empty, token);
        retained.Add(original);
        await PartitionMovementLifetimeProofRf3Calls.CompleteAsync(seed, terminal, original, token);
    }

    private static async Task<PartitionMovementPublicParentRf3NativeCut[]> StoppedAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal, PartitionMovementLifetimeProofRf3Original[] originals)
    {
        _ = await wave.StopForDirectoryReadAsync();
        var cuts = TwoRf3MembershipProtocol.Nodes.Select(node => PartitionMovementPublicParentRf3Cut.ReadStopped(
            wave, node, seed.FirstRequest, Guid.Empty)).ToArray();
        foreach (var original in originals)
        {
            await PartitionMovementLifetimeProofRf3Assertions.DurableAsync(seed, terminal, original, cuts);
        }
        return cuts;
    }
}
