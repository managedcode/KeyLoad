using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Cold
{
    private const int FirstEntry = 0;
    internal static async Task ReplayAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, PartitionMoveRequest[] requests,
        PartitionMoveResult[] terminals, CancellationToken token)
    {
        var before = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        foreach (var cut in before)
        {
            await PartitionMovementPublicParentRf3Cut.RequireCompactedAsync(cut);
            foreach (var owned in cut.Where(value => value.Header is not null))
            {
                await Assert.That(owned.Header!.OperatorPrincipalId).IsEqualTo(PhysicalShardAuthorityBoundaryRf3Actor.PrincipalId);
            }
        }
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await ReconnectAsync(wave, seeds, actor, token);
        var replay = await PhysicalShardAuthorityBoundaryRf3Execution.ExecuteAsync(actor, requests, token);
        await SqlRf3Protocol.EqualAsync(terminals, replay);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardAuthorityBoundaryRf3ParentReplay.RequireAsync(actor, seeds[index], requests[index], terminals[index], token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var after = await PhysicalShardPartitionMovementRf3Cold.StopAndReadAsync(wave, requests, token);
        await SqlRf3Protocol.EqualAsync(before, after);
        await PartitionMovementPublicParentRf3Cut.RestartAsync(wave, token);
        await ReconnectAsync(wave, seeds, actor, token);
    }

    private static async Task ReconnectAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, CancellationToken token)
    {
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(wave, token);
        }
        await actor.ReconnectAsync(wave, token);
    }
}
