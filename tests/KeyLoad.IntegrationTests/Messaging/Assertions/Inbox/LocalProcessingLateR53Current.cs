using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class LocalProcessingLateR53Current
{
    internal static async Task RunAsync(LocalProcessingLateR53State state, KeyLoadClient sdk,
        McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        await InboxProcessingClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(new(state.Lane, LocalProcessingLateR53Protocol.Message), token)), state.Input);
        await InboxProcessingClusterAssertions.EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(state.Lane, LocalProcessingLateR53Protocol.Message), token))).Value, state.Input);
        await InboxProcessingClusterAssertions.EqualAsync(await PublicReadAssertions.MessageAsync(
            await sdk.InspectAsync(new(state.Output, LocalProcessingLateR53Protocol.Message), token)), state.OutputImage);
        await InboxProcessingClusterAssertions.EqualAsync((await PublicReadAssertions.MessageAsync(await mcp.CallAsync(
            McpCallerTools.MessagesInspect, new InspectMessageRequest(state.Output, LocalProcessingLateR53Protocol.Message), token))).Value, state.OutputImage);
        await InboxProcessingClusterAssertions.EqualAsync(await PublicReadAssertions.DocumentAsync(
            await sdk.GetAsync(state.Document, token)), state.DocumentImage);
        await InboxProcessingClusterAssertions.EqualAsync((await PublicReadAssertions.DocumentAsync(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(state.Document), token))).Value, state.DocumentImage);
        await InboxProcessingClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.OutboxStatusAsync(state.Lane.Partition, token)), state.Outbox);
        await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await official.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Lane.Partition), token))).Value, state.Outbox);
    }

    internal static async Task OutboxAsync(LocalProcessingLateR53State state, KeyLoadClient admin,
        McpOfficialClient official, CancellationToken token)
    {
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.OutboxStatusAsync(state.Lane.Partition, token));
        await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await official.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Lane.Partition), token))).Value, state.Outbox);
    }

    internal static async Task ReceiveAsync(ReceiveRequest request,
        ReceiveResult result)
    {
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(request.Lane.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
        var delivery = await Assert.That(result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotNullOrEmpty();
        var expected = new ReceiveResult(request.RequestId,
            [new(LocalProcessingLateR53Protocol.Message, InboxProcessingClusterProtocol.Payload, "{}",
                delivery.Token, 1L, delivery.LeaseUntil, 1, 1L)], result.Token);
        await InboxProcessingClusterAssertions.EqualAsync(result, expected);
    }

    internal static async Task ReceiptAsync(LocalProcessingLateR53State state, CommitReceipt receipt,
        Guid originalId, MutationReceipt[] mutations)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(originalId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Lane.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await InboxProcessingClusterAssertions.EqualAsync(receipt.Mutations.ToArray(), mutations);
    }
}
