using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class RemoteTransferColdAssertions
{
    internal static async Task<QueueTransferInspection> SourceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferInspection? expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.TransferAsync(await sdk.InspectQueueTransferAsync(seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferColdProtocol.Missing);
        await Assert.That(actual.SourceQueue).IsEqualTo(seed.Scenario.SourceQueue);
        await Assert.That(actual.Destination).IsEqualTo(seed.Scenario.DestinationQueue);
        await Assert.That(actual.TransferId).IsEqualTo(seed.TransferId);
        await Assert.That(string.IsNullOrEmpty(actual.IntentToken)).IsFalse();
        if (expected is not null)
        {
            await SqlClusterProtocol.EqualAsync(expected, actual);
        }
        await SqlClusterProtocol.EqualAsync(actual, (await PublicReadAssertions.TransferAsync(
            await mcp.CallAsync(RemoteTransferColdProtocol.InspectSource, seed.SourceRequest, token))).Value);
        var sql = SqlClusterProtocol.Call(seed.Scenario.SourcePartition, RemoteTransferColdProtocol.InspectSource, seed.SourceRequest);
        await SqlClusterProtocol.EqualAsync(actual, (await SqlClusterProtocol.SdkAsync<QueueTransferReadResult>(sdk, sql, token)).Transfer);
        await SqlClusterProtocol.EqualAsync(actual, (await SqlClusterProtocol.McpAsync<QueueTransferReadResult>(mcp, sql, token)).Transfer);
        return actual;
    }

    internal static async Task<QueueTransferReceiptInspection?> ReceiptAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferReceiptInspection? expected, CancellationToken token)
    {
        var actual = await PublicReadAssertions.ReceiptAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
        await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.ReceiptAsync(
            await mcp.CallAsync(RemoteTransferColdProtocol.InspectReceipt, seed.ReceiptRequest, token))).Value);
        var sql = SqlClusterProtocol.Call(seed.Scenario.DestinationPartition, RemoteTransferColdProtocol.InspectReceipt, seed.ReceiptRequest);
        await SqlClusterProtocol.EqualAsync(expected, (await SqlClusterProtocol.SdkAsync<QueueTransferReceiptReadResult>(sdk, sql, token)).Receipt);
        await SqlClusterProtocol.EqualAsync(expected, (await SqlClusterProtocol.McpAsync<QueueTransferReceiptReadResult>(mcp, sql, token)).Receipt);
        return actual;
    }

    internal static async Task MessageAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(seed.Scenario.DestinationQueue, seed.Message.MessageId);
        await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.MessageAsync(
            await mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value);
        var sql = SqlClusterProtocol.Call(seed.Scenario.DestinationPartition, McpCallerTools.MessagesInspect, request);
        await SqlClusterProtocol.EqualAsync(expected, (await SqlClusterProtocol.SdkAsync<MessageInspectionResult>(sdk, sql, token)).Message);
        await SqlClusterProtocol.EqualAsync(expected, (await SqlClusterProtocol.McpAsync<MessageInspectionResult>(mcp, sql, token)).Message);
    }

    internal static MessageInspection Ready(RemoteTransferColdSeed seed, long sequence)
        => new(new(seed.Message.MessageId, MessageState.Ready, RemoteTransferColdProtocol.InitialAttempts,
            RemoteTransferColdProtocol.InitialStateVersion, sequence, null, null), seed.Message.PayloadJson, seed.Message.HeadersJson);

    internal static async Task ReceiptLiteralAsync(CommitReceipt receipt, CommandRequest command,
        QueueLaneRef lane, Guid transferId, string kind, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(new MutationReceipt(kind, lane.Queue,
            transferId.ToString(RemoteTransferColdProtocol.TransferIdFormat), revision), receipt.Mutations.Single());
    }

}
