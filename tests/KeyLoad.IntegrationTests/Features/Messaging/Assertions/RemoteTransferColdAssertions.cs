using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdAssertions
{
    internal static async Task<QueueTransferInspection> SourceAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferInspection? expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferAsync(seed.SourceRequest, token))
            ?? throw new InvalidOperationException(RemoteTransferColdProtocol.Missing);
        await Assert.That(actual.SourceQueue).IsEqualTo(seed.Scenario.SourceQueue);
        await Assert.That(actual.Destination).IsEqualTo(seed.Scenario.DestinationQueue);
        await Assert.That(actual.TransferId).IsEqualTo(seed.TransferId);
        await Assert.That(string.IsNullOrEmpty(actual.IntentToken)).IsFalse();
        if (expected is not null)
        { await SqlRf3Protocol.EqualAsync(expected, actual); }
        await SqlRf3Protocol.EqualAsync(actual, (await McpCallerAssertions.SuccessAsync<QueueTransferInspection?>(
            await mcp.CallAsync(RemoteTransferColdProtocol.InspectSource, seed.SourceRequest, token))).Value);
        var sql = SqlRf3Protocol.Call(seed.Scenario.SourcePartition, RemoteTransferColdProtocol.InspectSource, seed.SourceRequest);
        await SqlRf3Protocol.EqualAsync(actual, await SqlRf3Protocol.SdkAsync<QueueTransferInspection?>(sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(actual, await SqlRf3Protocol.McpAsync<QueueTransferInspection?>(mcp, sql, token));
        return actual;
    }

    internal static async Task<QueueTransferReceiptInspection?> ReceiptAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, QueueTransferReceiptInspection? expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<QueueTransferReceiptInspection?>(
            await mcp.CallAsync(RemoteTransferColdProtocol.InspectReceipt, seed.ReceiptRequest, token))).Value);
        var sql = SqlRf3Protocol.Call(seed.Scenario.DestinationPartition, RemoteTransferColdProtocol.InspectReceipt, seed.ReceiptRequest);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<QueueTransferReceiptInspection?>(sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<QueueTransferReceiptInspection?>(mcp, sql, token));
        return actual;
    }

    internal static async Task MessageAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(seed.Scenario.DestinationQueue, seed.Message.MessageId);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<MessageInspection?>(
            await mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value);
        var sql = SqlRf3Protocol.Call(seed.Scenario.DestinationPartition, McpCallerTools.MessagesInspect, request);
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.SdkAsync<MessageInspection?>(sdk, sql, token));
        await SqlRf3Protocol.EqualAsync(expected, await SqlRf3Protocol.McpAsync<MessageInspection?>(mcp, sql, token));
    }

    internal static MessageInspection Ready(RemoteTransferColdSeed seed, long sequence)
        => new(new(seed.Message.MessageId, MessageState.Ready, RemoteTransferColdProtocol.InitialAttempts,
            RemoteTransferColdProtocol.InitialStateVersion, sequence, null, null), seed.Message.PayloadJson, seed.Message.HeadersJson);

    internal static async Task ReceiptLiteralAsync(CommitReceipt receipt, CommandRequest command,
        QueueLaneRef lane, Guid transferId, string kind, long revision)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Mutations).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(new MutationReceipt(kind, lane.Queue,
            transferId.ToString(RemoteTransferColdProtocol.TransferIdFormat), revision), receipt.Mutations.Single());
    }

}
