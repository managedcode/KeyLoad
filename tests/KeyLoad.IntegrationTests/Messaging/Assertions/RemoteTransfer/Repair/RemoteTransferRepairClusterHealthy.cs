using KeyLoad.Client;
using KeyLoad.Core.Messaging;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class RemoteTransferRepairClusterHealthy
{
    internal static async Task ProveAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferRepairClusterOriginal original, bool acked, CancellationToken token)
    {
        var seed = original.Seed;
        var delivered = await RemoteTransferCoordinatorClusterWait.DeliveredAsync(sdk, seed, token);
        var proof = await PublicReadAssertions.ReceiptAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferRepairClusterProtocol.Missing);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, original.Intent with
        {
            State = QueueTransferState.Delivered,
            ReceiptToken = proof.ReceiptToken
        }, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
            acked ? new MessageInspection(new(seed.Message.MessageId, MessageState.Acked,
                RemoteTransferRepairClusterProtocol.ClaimedAttempts, RemoteTransferRepairClusterProtocol.AckedVersion,
                RemoteTransferRepairClusterProtocol.ReadySequence, null, null, LeaseVersion: RemoteTransferRepairClusterProtocol.OriginalLeaseVersion), null, null)
                : RemoteTransferColdAssertions.Ready(seed, RemoteTransferRepairClusterProtocol.ReadySequence), token);
        await SqlClusterProtocol.EqualAsync(delivered, original.Intent with
        {
            State = QueueTransferState.Delivered,
            ReceiptToken = proof.ReceiptToken
        });
        var hint = original.Hint with
        {
            AcceptPolicyGeneration = original.Stage == QueueTransferRepairStage.Accept
                ? RemoteTransferRepairClusterProtocol.RepairedGeneration : RemoteTransferRepairClusterProtocol.InitialGeneration,
            CompleteGeneration = original.Stage == QueueTransferRepairStage.Complete
                ? RemoteTransferRepairClusterProtocol.RepairedGeneration : RemoteTransferRepairClusterProtocol.InitialGeneration
        };
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            seed.Scenario.SourcePartition, [new CompleteQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, proof.ReceiptToken)]);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(complete, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(completed, complete, seed.Scenario.SourceQueue,
            seed.TransferId, RemoteTransferProtocol.CompleteReceiptKind, RemoteTransferColdProtocol.CompleteRevision);
        await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, complete, completed, token);
        if (original.Stage == QueueTransferRepairStage.Accept)
        {
            var accept = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
                seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, original.Intent.IntentToken)]);
            var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, token));
            await Assert.That(accepted.Token).IsEqualTo(proof.TargetCommit);
            await QueueProducerClusterAssertions.ReplayAsync(sdk, mcp, accept, accepted, token);
        }
        else
        {
            await SqlClusterProtocol.EqualAsync(proof, original.AcceptedProof);
        }
        await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(original.Failed, token), ErrorCode.PermissionDenied);
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Failed, token),
            ErrorCode.PermissionDenied, dispatched: true)).Value;
        await AtomicProducerClusterRoutes.RefusedAsync(sdk, mcp, original.Failed, ErrorCode.PermissionDenied, token);
        await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(seed.Create, token), ErrorCode.PermissionDenied);
    }
}
