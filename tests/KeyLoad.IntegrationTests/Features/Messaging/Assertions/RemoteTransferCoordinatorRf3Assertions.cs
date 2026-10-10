using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferCoordinatorRf3Assertions
{
    internal static RemoteTransferCoordinationHint Hint(RemoteTransferColdSeed seed, CommitReceipt created,
        QueueTransferInspection intent)
        => new(seed.Scenario.SourceQueue, seed.Scenario.DestinationQueue, seed.TransferId, seed.Identity.Principal.Id,
            JsonData.Fingerprint(seed.Message), RemoteTransferCoordinationIdentity.IntentDigest(intent.IntentToken), created.Token);

    internal static async Task<CommandRequest> FailedAcceptAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, RemoteTransferCoordinationHint hint, QueueTransferInspection intent, CancellationToken token)
    {
        var command = new CommandRequest(RemoteTransferCoordinationIdentity.CommandId(hint,
            RemoteTransferCoordinationProtocol.AcceptStage), seed.Scenario.DestinationPartition,
            [new AcceptQueueTransfer(seed.Scenario.DestinationQueue, intent.IntentToken)]);
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(command, token), ErrorCode.ResourceExhausted);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token),
            ErrorCode.ResourceExhausted, dispatched: true);
        await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, command, ErrorCode.ResourceExhausted, token);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, null, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, null, token);
        return command;
    }

    internal static async Task<CommitReceipt> CompletedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, RemoteTransferCoordinationHint hint, QueueTransferInspection delivered,
        long sequence, CancellationToken token)
    {
        var proof = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferCoordinatorRf3Protocol.Missing);
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, delivered, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, proof, token);
        await Assert.That(delivered.ReceiptToken).IsEqualTo(proof.ReceiptToken);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, RemoteTransferColdAssertions.Ready(seed, sequence), token);
        var complete = new CommandRequest(RemoteTransferCoordinationIdentity.CommandId(hint,
            RemoteTransferCoordinationProtocol.CompleteStage), seed.Scenario.SourcePartition,
            [new CompleteQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, proof.ReceiptToken)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(complete, token));
        await RemoteTransferColdAssertions.ReceiptLiteralAsync(receipt, complete, seed.Scenario.SourceQueue, seed.TransferId,
            RemoteTransferColdProtocol.CompleteKind, RemoteTransferCoordinatorRf3Protocol.CompleteRevision);
        await QueueProducerRf3Assertions.ReplayAsync(sdk, mcp, complete, receipt, token);
        return receipt;
    }
}
