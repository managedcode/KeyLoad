using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueEmptyHistoryPublicAssertions
{
    internal const string First = "empty-history-first";
    internal const string Sibling = "empty-history-sibling";

    internal static async Task OriginalAsync(RequestCqrsRf3Callers callers,
        QueueEmptyHistoryPublicState state, CancellationToken token)
    {
        await Assert.That(state.EmptyResult.RequestId).IsEqualTo(state.EmptyRequest.RequestId);
        await Assert.That(state.EmptyResult.Deliveries).IsEmpty();
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            var replay = await QueueLifecyclePublicRoutes.CallAsync(callers, route, state.Owner.Partition,
                McpCallerTools.MessagesReceive, state.EmptyRequest,
                () => callers.Sdk.ReceiveAsync(state.EmptyRequest, token), token);
            await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(state.EmptyResult))).IsTrue();
        }
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Owner, token);
    }

    internal static async Task ChangedAsync(RequestCqrsRf3Callers callers,
        QueueEmptyHistoryPublicState state, CancellationToken token)
    {
        var changed = state.EmptyRequest with
        {
            MaxMessages = 2
        };
        var refusal = await callers.Sdk.ReceiveAsync(changed, token);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(refusal, ErrorCode.Conflict);
        await Assert.That(refusal.Value).IsNull();
        var problem = JsonSerializer.SerializeToElement(refusal.Problem, JsonDefaults.Options);
        if (state.ChangedProblem is { } saved)
        {
            await Assert.That(JsonDefaults.Serialize(problem).SequenceEqual(JsonDefaults.Serialize(saved))).IsTrue();
        }
        else
        {
            state.ChangedProblem = problem;
        }
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, changed, token), ErrorCode.Conflict, true);
    }

    internal static Task MessageAsync(RequestCqrsRf3Callers callers, QueueEmptyHistoryPublicState state,
        string id, MessageMetadata metadata, bool body, CancellationToken token)
        => QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, id,
            new(metadata, body ? QueueLifecyclePublicProtocol.CanonicalPayload : null,
                body ? QueueLifecyclePublicProtocol.Headers : null), token);

    internal static async Task ReadyAsync(RequestCqrsRf3Callers callers,
        QueueEmptyHistoryPublicState state, bool firstSettled, CancellationToken token)
    {
        await MessageAsync(callers, state, First, firstSettled
            ? new(First, MessageState.Acked, 1, 3L, 1L, null, null, LeaseVersion: 1L)
            : new(First, MessageState.Ready, 0, 1L, 1L, null, null), !firstSettled, token);
        await MessageAsync(callers, state, Sibling,
            new(Sibling, MessageState.Ready, 0, 1L, 2L, null, null), true, token);
    }

    internal static async Task TerminalAsync(RequestCqrsRf3Callers callers, QueueEmptyHistoryPublicState state,
        bool healthy, CancellationToken token)
    {
        await MessageAsync(callers, state, First,
            new(First, MessageState.Acked, 1, 3L, 1L, null, null, LeaseVersion: 1L), false, token);
        await MessageAsync(callers, state, Sibling,
            new(Sibling, MessageState.Acked, 1, 3L, 2L, null, null, LeaseVersion: 1L), false, token);
        if (healthy)
        {
            await MessageAsync(callers, state, QueueLifecyclePublicProtocol.Healthy,
            new(QueueLifecyclePublicProtocol.Healthy, MessageState.Acked, 1, 3L, 3L, null, null, LeaseVersion: 1L), false, token);
        }
    }
}
