using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxClusterFinish
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxClusterState state, CancellationToken token)
    {
        var second = state.Original with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = TargetInboxClusterProtocol.ReceiptCapacity,
            Effects = []
        };
        var result = await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(second, token));
        await Assert.That(result.AlreadyProcessed).IsFalse();
        var refused = second with
        {
            CommandId = Guid.NewGuid(),
            ExecutionGeneration = checked(TargetInboxClusterProtocol.ReceiptCapacity + TargetInboxClusterProtocol.Generation),
            Effects = [new PutDocument(TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Changed, TargetInboxClusterProtocol.Payload)]
        };
        await TargetInboxClusterAssertions.DeniedAsync(await client.CommitInboxAsync(refused, token), ErrorCode.ResourceExhausted);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxClusterProtocol.Tool, refused, token), ErrorCode.ResourceExhausted, true);
        await TargetInboxClusterAssertions.UnchangedAsync(client, state, token);
        await TargetInboxClusterReplay.ExecuteAsync(client, mcp, state, false, token);
        var stale = state.SourceAck with
        {
            CommandId = Guid.NewGuid()
        };
        await TargetInboxClusterAssertions.DeniedAsync(await client.CompleteAsync(stale, token), ErrorCode.StaleLease);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesComplete,
            stale, token), ErrorCode.StaleLease, true);
        await TargetInboxClusterAssertions.UnchangedAsync(client, state, token);
        var outputLane = new QueueLaneRef(state.Target.Partition, TargetInboxClusterProtocol.Output);
        var output = await McpCallerAssertions.SdkSuccessAsync(await client.ReceiveAsync(new(Guid.NewGuid(), outputLane), token));
        var delivery = await Assert.That(output.Deliveries).HasSingleItem();
        _ = await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(new(Guid.NewGuid(), outputLane, delivery.Token, DeliveryAction.Ack), token));
        await Assert.That((await PublicReadAssertions.MessageAsync(await client.InspectAsync(new(outputLane, delivery.Id), token)))!.Metadata.State).IsEqualTo(MessageState.Acked);
    }
}
