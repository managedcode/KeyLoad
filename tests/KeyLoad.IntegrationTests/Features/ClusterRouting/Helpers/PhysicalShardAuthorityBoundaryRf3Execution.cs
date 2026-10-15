using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Execution
{
    private const int FirstEntry = 0;
    internal static async Task<PartitionMoveResult[]> ExecuteAsync(PhysicalShardAuthorityBoundaryRf3Actor actor,
        PartitionMoveRequest[] requests, CancellationToken token)
    {
        var result = new List<PartitionMoveResult>();
        await foreach (var reply in actor.Sdk.MoveWholePartitionsAsync(requests, token))
        {
            result.Add(await McpCallerAssertions.SdkSuccessAsync(reply));
        }
        await Assert.That(result.Count).IsEqualTo(requests.Length);
        for (var index = FirstEntry; index < requests.Length; index++)
        {
            await Assert.That(result[index].MoveId).IsEqualTo(requests[index].MoveId);
            await Assert.That(result[index].Partition).IsEqualTo(requests[index].Partition);
            await Assert.That(result[index].Phase).IsEqualTo(PartitionMovePhase.Retired);
        }
        return result.ToArray();
    }
}
