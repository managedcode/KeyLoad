using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferDistinctHealthy
{
    internal static async Task ExecuteAsync(TwoRf3MembershipWave wave, RemoteTransferColdSeed original,
        CancellationToken token)
    {
        var transferId = Guid.NewGuid();
        var message = original.Message with
        { MessageId = RemoteTransferColdProtocol.Healthy, PayloadJson = RemoteTransferColdProtocol.HealthyPayload };
        var create = new CommandRequest(Guid.NewGuid(), original.Scenario.SourcePartition,
            [new CreateQueueTransfer(original.Scenario.SourceQueue, transferId, original.Scenario.DestinationQueue, message)]);
        var healthy = original with { TransferId = transferId, Message = message, Create = create };
        await RemoteTransferDistinctCallers.WithAsync(wave, healthy, async (sdk, mcp) =>
        {
            var source = await RemoteTransferColdStages.CreateAsync(sdk, mcp, healthy, token);
            var accept = healthy.Accept(source.Intent);
            var target = await RemoteTransferColdStages.AcceptAsync(sdk, mcp, healthy, source.Intent, accept,
                RemoteTransferColdProtocol.HealthyReadySequence, token);
            await RemoteTransferColdStages.CompleteAsync(sdk, mcp, healthy, source.Intent, target.Proof,
                healthy.Complete(target.Proof), token);
            await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, healthy,
                RemoteTransferColdAssertions.Ready(healthy, RemoteTransferColdProtocol.HealthyReadySequence), token);
        }, token);
    }
}
