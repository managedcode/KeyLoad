using KeyLoad.Client;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferAttemptRf3Healthy
{
    internal static async Task ProveAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        RemoteTransferCoordinationHint hint, QueueTransferInspection original, CommandRequest failed,
        int ceiling, CancellationToken token)
    {
        var delivered = await RemoteTransferCoordinatorRf3Wait.DeliveredAsync(sdk, seed, token);
        var receipt = await RemoteTransferCoordinatorRf3Assertions.CompletedAsync(sdk, mcp, seed, hint, delivered,
            RemoteTransferCoordinatorRf3Protocol.RepairedReady, token);
        if (ceiling == RemoteTransferAttemptRf3Protocol.AutomaticCeiling)
        {
            var accept = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, RemoteTransferAttemptRf3Protocol.AdvancedGeneration),
                seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, original.IntentToken)]);
            var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, token));
            var proof = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
                ?? throw new InvalidOperationException(RemoteTransferCoordinatorRf3Protocol.Missing);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
            await Assert.That(accepted.Token).IsEqualTo(proof.TargetCommit);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, accepted, token);
        }
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, failed, token),
            ErrorCode.ResourceExhausted, dispatched: true);
        await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, failed, ErrorCode.ResourceExhausted, token);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Scenario.SourcePartition.AtomicPartitionId);
    }
}
