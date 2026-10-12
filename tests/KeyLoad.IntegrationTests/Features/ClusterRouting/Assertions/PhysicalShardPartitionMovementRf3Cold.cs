using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3Cold
{
    private const int FirstEntry = 0;
    private const int SecondEntry = 1;
    internal static async Task RequireUnstartedAndReopenAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests, CancellationToken token)
    {
        var first = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, requests[FirstEntry], token);
        await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(first);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var untouched = PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, requests[SecondEntry], Guid.Empty);
            await Assert.That(untouched.Header).IsNull();
            await Assert.That(untouched.Pending).IsNull();
            await Assert.That(untouched.LastIssued).IsNull();
        }
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
    }

    internal static async Task ReplayAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests,
        PartitionMoveResult[] terminals, CancellationToken token)
    {
        var before = await StopAndReadAsync(wave, requests, token);
        foreach (var cut in before)
        { await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cut); }
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        var replay = await PhysicalShardPartitionMovementRf3Flow.ExecuteAsync(seeds, requests, token);
        await SqlRf3Protocol.EqualAsync(terminals, replay);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PartitionMovementPublicCallerReplay.RequireAsync(seeds[index].Source, seeds[index].Official,
                requests[index], terminals[index], token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var after = await StopAndReadAsync(wave, requests, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
    }

    internal static async Task<PartitionMovementPublicParentRf3NativeCut[][]> StopAndReadAsync(
        TwoRf3MembershipWave wave, PartitionMoveRequest[] requests, CancellationToken token)
    {
        var cuts = new PartitionMovementPublicParentRf3NativeCut[requests.Length][];
        cuts[FirstEntry] = await PartitionMovementPublicParentRf3Cut.StopAndReadAsync(wave, requests[FirstEntry], token);
        for (var index = SecondEntry; index < requests.Length; index++)
        {
            var request = requests[index];
            cuts[index] = TwoRf3MembershipProtocol.Nodes.Select(node =>
                PartitionMovementPublicParentRf3Cut.ReadStopped(wave, node, request, Guid.Empty)).ToArray();
        }
        return cuts;
    }
}
