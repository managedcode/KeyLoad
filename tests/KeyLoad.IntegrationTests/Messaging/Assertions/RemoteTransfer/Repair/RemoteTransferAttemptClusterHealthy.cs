using KeyLoad.Client;
using KeyLoad.Core.Messaging;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class RemoteTransferAttemptClusterHealthy
{
    internal static async Task ProveAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        RemoteTransferCoordinationHint hint, QueueTransferInspection original, CommandRequest failed,
        int ceiling, CancellationToken token)
    {
        var delivered = await RemoteTransferCoordinatorClusterWait.DeliveredAsync(sdk, seed, token);
        var receipt = await RemoteTransferCoordinatorClusterAssertions.CompletedAsync(sdk, mcp, seed, hint, delivered,
            RemoteTransferCoordinatorClusterProtocol.RepairedReady, token);
        if (ceiling == RemoteTransferAttemptClusterProtocol.AutomaticCeiling)
        {
            var accept = new CommandRequest(RemoteTransferAttemptIdentity.AcceptId(hint, RemoteTransferAttemptClusterProtocol.AdvancedGeneration),
                seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, original.IntentToken)]);
            var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, token));
            var proof = await PublicReadAssertions.ReceiptAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
                ?? throw new InvalidOperationException(RemoteTransferCoordinatorClusterProtocol.Missing);
            await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
            await Assert.That(accepted.Token).IsEqualTo(proof.TargetCommit);
            await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, accept, accepted, token);
        }
        await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(failed, token), ErrorCode.ResourceExhausted);
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, failed, token),
            ErrorCode.ResourceExhausted, dispatched: true)).Value;
        await AtomicProducerClusterRoutes.RefusedAsync(sdk, mcp, failed, ErrorCode.ResourceExhausted, token);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(seed.Scenario.SourcePartition.AtomicPartitionId);
    }
}
