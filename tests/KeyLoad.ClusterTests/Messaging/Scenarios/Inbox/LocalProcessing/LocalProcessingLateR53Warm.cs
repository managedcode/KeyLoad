using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class LocalProcessingLateR53Warm
{
    internal static async Task<LocalProcessingLateR53State> RunAsync(ClusterFixture fixture, QueueLaneRef lane,
        MessagingClusterIdentity identity, EntityRefLifetime collectionLifetime, CancellationToken token)
    {
        var state = new LocalProcessingLateR53State(lane, identity)
        {
            CollectionLifetime = collectionLifetime
        };
        await LocalProcessingLateR53Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            var seed = new CommandRequest(Guid.NewGuid(), lane.Partition,
                [new EnqueueMessage(lane.Queue, LocalProcessingLateR53Protocol.Message, InboxProcessingClusterProtocol.Payload)]);
            var seeded = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(seed, token));
            await LocalProcessingLateR53Current.ReceiptAsync(state, seeded, seed.CommandId,
                [new(LocalProcessingLateR53Protocol.EnqueueKind, lane.Queue, LocalProcessingLateR53Protocol.Message, 1L)]);
            state.Input = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Ready, 0, 1, 2,
                null, null), InboxProcessingClusterProtocol.Payload, "{}");
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await official.CallAsync(McpCallerTools.DocumentsCommit, seed, token))).Value, seeded);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            var receive = new ReceiveRequest(Guid.NewGuid(), lane);
            var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(receive, token));
            await LocalProcessingLateR53Current.ReceiveAsync(receive, received);
            var delivery = await VerifiedDeliveryAsync(received);
            state.Input = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Leased, 1, 2, 2,
                null, null, identity.Principal.Id, 1, delivery.LeaseUntil), InboxProcessingClusterProtocol.Payload, "{}");
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
                await mcp.CallAsync(McpCallerTools.MessagesReceive, receive, token))).Value, received);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            state.Failed = new(Guid.NewGuid(), lane, delivery.Token, InboxProcessingClusterProtocol.Handler,
                InboxProcessingClusterProtocol.Generation,
                [new PutDocument(InboxProcessingClusterProtocol.Collection, LocalProcessingLateR53Protocol.Document,
                    InboxProcessingClusterProtocol.Payload, 0),
                 new EnqueueMessage(InboxProcessingClusterProtocol.Output, LocalProcessingLateR53Protocol.Message, InboxProcessingClusterProtocol.Payload),
                 new PutDocument(InboxProcessingClusterProtocol.Collection, LocalProcessingLateR53Protocol.Document,
                    InboxProcessingClusterProtocol.Payload, InboxProcessingClusterProtocol.MissingRevision)]);
            state.OriginalFailure = await sdk.CommitProcessingAsync(state.Failed, token);
            await LocalProcessingLateR53Failures.SdkAsync(state.OriginalFailure, ErrorCode.RevisionConflict, LocalProcessingLateR53Protocol.RevisionDetail);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Failures.McpAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Failed, token), ErrorCode.RevisionConflict);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await SucceedAsync(state, lane, sdk, mcp, admin, official, token);
        }, token);
        return state;
    }

    private static async Task<Delivery> VerifiedDeliveryAsync(ReceiveResult received)
    {
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(LocalProcessingLateR53Protocol.Message);
        await Assert.That(delivery.PayloadJson).IsEqualTo(InboxProcessingClusterProtocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        return delivery;
    }

    private static async Task SucceedAsync(LocalProcessingLateR53State state, QueueLaneRef lane,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        state.Success = state.Failed with
        {
            CommandId = Guid.NewGuid(),
            Effects = [state.Failed.Effects[0], state.Failed.Effects[1]]
        };
        state.Receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitProcessingAsync(state.Success, token));
        await LocalProcessingLateR53Current.ReceiptAsync(state, state.Receipt, state.Success.CommandId,
            [new(LocalProcessingLateR53Protocol.PutKind, InboxProcessingClusterProtocol.Collection, LocalProcessingLateR53Protocol.Document, 1L),
             new(LocalProcessingLateR53Protocol.EnqueueKind, InboxProcessingClusterProtocol.Output, LocalProcessingLateR53Protocol.Message, 1L),
             new(LocalProcessingLateR53Protocol.AckKind, lane.Queue, LocalProcessingLateR53Protocol.Message, 3L)]);
        state.Input = new(state.Input.Metadata with
        {
            State = MessageState.Acked,
            StateVersion = 3,
            LeaseOwner = null,
            LeaseUntil = null
        }, null, null);
        state.OutputImage = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Ready, 0, 1, 2, null, null), InboxProcessingClusterProtocol.Payload, "{}");
        state.DocumentImage = new(state.StoredDocument, 1, InboxProcessingClusterProtocol.Payload, false, []);
        await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
        await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Success, token))).Value, state.Receipt);
        await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
        await LocalProcessingLateR53Replay.RunAsync(state, sdk, mcp, admin, official, token);
    }
}
