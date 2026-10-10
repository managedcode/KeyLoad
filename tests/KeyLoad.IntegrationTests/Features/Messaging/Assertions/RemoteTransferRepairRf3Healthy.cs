using KeyLoad.Client;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferRepairRf3Healthy
{
    internal static async Task ProveAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferRepairRf3Original original, bool acked, CancellationToken token)
    {
        var seed = original.Seed;
        var delivered = await RemoteTransferCoordinatorRf3Wait.DeliveredAsync(sdk, seed, token);
        var proof = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferRepairRf3Protocol.Missing);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, original.Intent with
        { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken }, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed,
            acked ? new MessageInspection(new(seed.Message.MessageId, MessageState.Acked,
                RemoteTransferRepairRf3Protocol.ClaimedAttempts, RemoteTransferRepairRf3Protocol.AckedVersion,
                RemoteTransferRepairRf3Protocol.ReadySequence, null, null, LeaseVersion: RemoteTransferRepairRf3Protocol.OriginalLeaseVersion), null, null)
                : RemoteTransferColdAssertions.Ready(seed, RemoteTransferRepairRf3Protocol.ReadySequence), token);
        await SqlRf3Protocol.EqualAsync(delivered, original.Intent with
        { State = QueueTransferState.Delivered, ReceiptToken = proof.ReceiptToken });
        var hint = original.Hint with
        {
            AcceptPolicyGeneration = original.Stage == QueueTransferRepairStage.Accept
                ? RemoteTransferRepairRf3Protocol.RepairedGeneration : RemoteTransferRepairRf3Protocol.InitialGeneration,
            CompleteGeneration = original.Stage == QueueTransferRepairStage.Complete
                ? RemoteTransferRepairRf3Protocol.RepairedGeneration : RemoteTransferRepairRf3Protocol.InitialGeneration
        };
        var complete = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Complete),
            seed.Scenario.SourcePartition, [new CompleteQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, proof.ReceiptToken)]);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(complete, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(completed, complete, seed.Scenario.SourceQueue,
            seed.TransferId, RemoteTransferProtocol.CompleteReceiptKind, RemoteTransferRepairRf3Protocol.InitialGeneration);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, complete, completed, token);
        if (original.Stage == QueueTransferRepairStage.Accept)
        {
            var accept = new CommandRequest(RemoteTransferRepairIdentity.CommandId(hint, QueueTransferRepairStage.Accept),
                seed.Scenario.DestinationPartition, [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, original.Intent.IntentToken)]);
            var accepted = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(accept, token));
            await Assert.That(accepted.Token).IsEqualTo(proof.TargetCommit);
            await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, accept, accepted, token);
        }
        else
        { await SqlRf3Protocol.EqualAsync(proof, original.AcceptedProof); }
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(original.Failed, token), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, original.Failed, token),
            ErrorCode.PermissionDenied, dispatched: true);
        await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, original.Failed, ErrorCode.PermissionDenied, token);
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(seed.Create, token), ErrorCode.PermissionDenied);
    }
}
