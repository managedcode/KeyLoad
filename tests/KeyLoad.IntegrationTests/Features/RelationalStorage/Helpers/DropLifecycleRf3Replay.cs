using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class DropLifecycleRf3Replay
{
    internal static async Task AllAsync(RequestCqrsRf3Callers caller, Guid parentId, DropTableRequest request,
        PartitionRef partition, DropLifecycleRf3ReceiptProof proof, DropTableResult original, CancellationToken token,
        Func<Task>? unchanged = null)
    {
        await Assert.That(proof.FinalizationCommandId).IsNotEqualTo(parentId);
        await DropLifecycleRf3Assertions.ResultAsync(request, proof, original);
        foreach (var route in Enum.GetValues<DropLifecycleRf3Route>())
        {
            token.ThrowIfCancellationRequested();
            var replay = await DropLifecycleRf3Calls.InvokeAsync(caller, parentId, request, partition, route, token);
            await DropLifecycleRf3Assertions.ResultAsync(request, proof, replay);
            await SqlRf3Protocol.EqualAsync(original, replay);
            if (unchanged is not null)
            {
                await unchanged();
            }
        }
    }
}
