using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class LocalProcessingLateR53Flow
{
    internal static Task FirstColdAsync(ClusterFixture fixture, LocalProcessingLateR53State state, CancellationToken token)
        => LocalProcessingLateR53Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);

    internal static Task RestoredAsync(ClusterFixture fixture, LocalProcessingLateR53State state, CancellationToken token)
    {
        state.OriginalAllowed = false;
        return FirstColdAsync(fixture, state, token);
    }

    internal static Task FinishAsync(ClusterFixture fixture, LocalProcessingLateR53State state, CancellationToken token)
        => LocalProcessingLateR53Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Replay.RunAsync(state, sdk, mcp, admin, official, token);
            var receive = new ReceiveRequest(Guid.NewGuid(), state.Output);
            var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(receive, token));
            await LocalProcessingLateR53Current.ReceiveAsync(receive, received);
            var delivery = await Assert.That(received.Deliveries).HasSingleItem();
            await Assert.That(delivery.Id).IsEqualTo(LocalProcessingLateR53Protocol.Message);
            await Assert.That(delivery.PayloadJson).IsEqualTo(InboxProcessingClusterProtocol.Payload);
            await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
            state.OutputImage = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Leased, 1, 2, 2,
                null, null, state.Identity.Principal.Id, 1, delivery.LeaseUntil), InboxProcessingClusterProtocol.Payload, "{}");
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
                await mcp.CallAsync(McpCallerTools.MessagesReceive, receive, token))).Value, received);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            state.OutputProcessing = new(Guid.NewGuid(), state.Output, delivery.Token, InboxProcessingClusterProtocol.Handler,
                InboxProcessingClusterProtocol.Generation, []);
            state.OutputReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitProcessingAsync(state.OutputProcessing, token));
            await LocalProcessingLateR53Current.ReceiptAsync(state, state.OutputReceipt, state.OutputProcessing.CommandId,
                [new(LocalProcessingLateR53Protocol.AckKind, state.Output.Queue, LocalProcessingLateR53Protocol.Message, 3L)]);
            state.OutputImage = new(state.OutputImage.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = 3,
                LeaseOwner = null,
                LeaseUntil = null
            }, null, null);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.MessagesProcess, state.OutputProcessing, token))).Value, state.OutputReceipt);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
}
