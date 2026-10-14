using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class LocalProcessingLateR53Warm
{
    internal static async Task<LocalProcessingLateR53State> RunAsync(ClusterFixture fixture, QueueLaneRef lane,
        MessagingRf3Identity identity, CancellationToken token)
    {
        var state = new LocalProcessingLateR53State(lane, identity);
        await LocalProcessingLateR53Callers.RunAsync(fixture, state, async (sdk, mcp, admin, official) =>
        {
            var placement = new AtomicPartitionPlacementReadRequest(1, lane.Partition);
            state.Placement = await McpCallerAssertions.SdkSuccessAsync(await admin.ReadAtomicPartitionPlacementAsync(placement, token));
            await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await official.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value, state.Placement);
            var seed = new CommandRequest(Guid.NewGuid(), lane.Partition,
                [new EnqueueMessage(lane.Queue, LocalProcessingLateR53Protocol.Message, InboxProcessingRf3Protocol.Payload)]);
            var seeded = await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(seed, token));
            await LocalProcessingLateR53Current.ReceiptAsync(state, seeded, seed.CommandId,
                [new(LocalProcessingLateR53Protocol.EnqueueKind, lane.Queue, LocalProcessingLateR53Protocol.Message, 1L)]);
            state.Input = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Ready, 0, 1, 2,
                null, null), InboxProcessingRf3Protocol.Payload, "{}");
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await official.CallAsync(McpCallerTools.DocumentsCommit, seed, token))).Value, seeded);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            var receive = new ReceiveRequest(Guid.NewGuid(), lane);
            var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(receive, token));
            await LocalProcessingLateR53Current.ReceiveAsync(state, receive, received);
            var delivery = await VerifiedDeliveryAsync(received);
            state.Input = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Leased, 1, 2, 2,
                null, null, identity.Principal.Id, 1, delivery.LeaseUntil), InboxProcessingRf3Protocol.Payload, "{}");
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
                await mcp.CallAsync(McpCallerTools.MessagesReceive, receive, token))).Value, received);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            state.Failed = new(Guid.NewGuid(), lane, delivery.Token, InboxProcessingRf3Protocol.Handler,
                InboxProcessingRf3Protocol.Generation,
                [new PutDocument(InboxProcessingRf3Protocol.Collection, LocalProcessingLateR53Protocol.Document,
                    InboxProcessingRf3Protocol.Payload, 0),
                 new EnqueueMessage(InboxProcessingRf3Protocol.Output, LocalProcessingLateR53Protocol.Message, InboxProcessingRf3Protocol.Payload),
                 new PutDocument(InboxProcessingRf3Protocol.Collection, LocalProcessingLateR53Protocol.Document,
                    InboxProcessingRf3Protocol.Payload, InboxProcessingRf3Protocol.MissingRevision)]);
            state.OriginalFailure = await sdk.CommitProcessingAsync(state.Failed, token);
            await LocalProcessingLateR53Failures.SdkAsync(state.OriginalFailure, ErrorCode.RevisionConflict, LocalProcessingLateR53Protocol.RevisionDetail);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Failures.McpAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Failed, token), ErrorCode.RevisionConflict);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            state.Success = state.Failed with
            {
                CommandId = Guid.NewGuid(),
                Effects = [state.Failed.Effects[0], state.Failed.Effects[1]]
            };
            state.Receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitProcessingAsync(state.Success, token));
            await LocalProcessingLateR53Current.ReceiptAsync(state, state.Receipt, state.Success.CommandId,
                [new(LocalProcessingLateR53Protocol.PutKind, InboxProcessingRf3Protocol.Collection, LocalProcessingLateR53Protocol.Document, 1L),
                 new(LocalProcessingLateR53Protocol.EnqueueKind, InboxProcessingRf3Protocol.Output, LocalProcessingLateR53Protocol.Message, 1L),
                 new(LocalProcessingLateR53Protocol.AckKind, lane.Queue, LocalProcessingLateR53Protocol.Message, 3L)]);
            state.Input = new(state.Input.Metadata with
            {
                State = MessageState.Acked,
                StateVersion = 3,
                LeaseOwner = null,
                LeaseUntil = null
            }, null, null);
            state.OutputImage = new(new(LocalProcessingLateR53Protocol.Message, MessageState.Ready, 0, 1, 2, null, null), InboxProcessingRf3Protocol.Payload, "{}");
            state.DocumentImage = new(state.Document, 1, InboxProcessingRf3Protocol.Payload, false, []);
            await LocalProcessingLateR53Current.OutboxAsync(state, admin, official, token);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Success, token))).Value, state.Receipt);
            await LocalProcessingLateR53Current.RunAsync(state, sdk, mcp, admin, official, token);
            await LocalProcessingLateR53Replay.RunAsync(state, sdk, mcp, admin, official, token);
        }, token);
        return state;
    }

    private static async Task<Delivery> VerifiedDeliveryAsync(ReceiveResult received)
    {
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(LocalProcessingLateR53Protocol.Message);
        await Assert.That(delivery.PayloadJson).IsEqualTo(InboxProcessingRf3Protocol.Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo("{}");
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        return delivery;
    }
}
