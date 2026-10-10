using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferPostAwaitAssertions
{
    internal static async Task StateAsync(RemoteTransferPostAwaitCallers calls, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, QueueTransferReceiptInspection? proof, MessageInspection? message,
        CancellationToken token)
    {
        await calls.RequireAsync(seed.Scenario.SourcePartition, RemoteTransferColdProtocol.InspectSource,
            seed.SourceRequest, ct => calls.Sdk.InspectQueueTransferAsync(seed.SourceRequest, ct), intent, token);
        await calls.RequireAsync(seed.Scenario.DestinationPartition, RemoteTransferColdProtocol.InspectReceipt,
            seed.ReceiptRequest, ct => calls.Sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, ct), proof, token);
        var request = new InspectMessageRequest(seed.Scenario.DestinationQueue, seed.Message.MessageId);
        await calls.RequireAsync(seed.Scenario.DestinationPartition, McpCallerTools.MessagesInspect,
            request, ct => calls.Sdk.InspectAsync(request, ct), message, token);
    }

    internal static async Task<QueueTransferReceiptInspection> ReceiptAsync(RemoteTransferPostAwaitCallers calls,
        RemoteTransferColdSeed seed, CommitReceipt receipt, CancellationToken token)
    {
        var proof = await McpCallerAssertions.SdkSuccessAsync(await calls.Sdk.InspectQueueTransferReceiptAsync(
            seed.ReceiptRequest, token)) ?? throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
        await Assert.That(proof.SourceQueue).IsEqualTo(seed.Scenario.SourceQueue);
        await Assert.That(proof.DestinationQueue).IsEqualTo(seed.Scenario.DestinationQueue);
        await Assert.That(proof.TransferId).IsEqualTo(seed.TransferId);
        await Assert.That(string.IsNullOrEmpty(proof.ReceiptToken)).IsFalse();
        await SqlRf3Protocol.EqualAsync(receipt.Token, proof.TargetCommit);
        return proof;
    }

    internal static async Task OldCreateDeniedAsync(RemoteTransferPostAwaitCallers calls,
        RemoteTransferColdSeed seed, CancellationToken token)
    {
        await QueueProducerRf3Assertions.DeniedAsync(await calls.Sdk.CommitAsync(seed.Create, token), ErrorCode.PermissionDenied);
        var client = calls;
        await client.DeniedCommandAsync(seed.Create, ErrorCode.PermissionDenied, token);
    }
}
