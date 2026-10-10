using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Finish
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxRf3State state, CancellationToken token)
    {
        var second = state.Original with { CommandId = Guid.NewGuid(), ExecutionGeneration = TargetInboxRf3Protocol.ReceiptCapacity, Effects = [] };
        var result = await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(second, token));
        await Assert.That(result.AlreadyProcessed).IsFalse();
        var refused = second with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = checked(TargetInboxRf3Protocol.ReceiptCapacity + TargetInboxRf3Protocol.Generation),
            Effects = [new PutDocument(TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Changed, TargetInboxRf3Protocol.Payload)]
        };
        await TargetInboxRf3Assertions.DeniedAsync(await client.CommitInboxAsync(refused, token), ErrorCode.ResourceExhausted);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxRf3Protocol.Tool, refused, token), ErrorCode.ResourceExhausted, true);
        await TargetInboxRf3Assertions.UnchangedAsync(client, state, token);
        await TargetInboxRf3Replay.ExecuteAsync(client, mcp, state, false, token);
        var stale = state.SourceAck with { CommandId = Guid.NewGuid() };
        await TargetInboxRf3Assertions.DeniedAsync(await client.CompleteAsync(stale, token), ErrorCode.StaleLease);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesComplete,
            stale, token), ErrorCode.StaleLease, true);
        await TargetInboxRf3Assertions.UnchangedAsync(client, state, token);
        var outputLane = new QueueLaneRef(state.Target.Partition, TargetInboxRf3Protocol.Output);
        var output = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), outputLane), token));
        var delivery = await Assert.That(output.Deliveries).HasSingleItem();
        _ = await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(new(Guid.NewGuid(), outputLane, delivery.Token, DeliveryAction.Ack), token));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await client.InspectAsync(new(outputLane, delivery.Id), token)))!.Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
