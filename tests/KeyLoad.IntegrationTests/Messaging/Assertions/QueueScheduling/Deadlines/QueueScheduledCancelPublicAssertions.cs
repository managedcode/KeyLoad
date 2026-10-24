using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueScheduledCancelPublicAssertions
{
    internal const string Target = "scheduled-cancel-target";
    internal const string Sibling = "scheduled-cancel-sibling";
    internal const string Replacement = "scheduled-cancel-replacement";
    internal const string Witness = "scheduled-cancel-witness";

    internal static Task MessageAsync(RequestCqrsClusterCallers callers, QueueScheduledCancelPublicState state,
        string id, MessageMetadata metadata, bool body, CancellationToken token)
        => QueueLeasedCancelPublicAssertions.MessageAsync(callers, state.Owner, id,
            new(metadata, body ? QueueLifecyclePublicProtocol.CanonicalPayload : null,
                body ? QueueLifecyclePublicProtocol.Headers : null), token);

    internal static async Task CutAsync(RequestCqrsClusterCallers callers, QueueScheduledCancelPublicState state,
        bool cancelled, CancellationToken token)
    {
        var metadata = cancelled
            ? new MessageMetadata(Target, MessageState.Cancelled, 0, 2L, 0L, null, null,
                LeaseVersion: 1L, DeliveryGeneration: 2L)
            : new MessageMetadata(Target, MessageState.Scheduled, 0, 1L, 0L, state.Due, null);
        await MessageAsync(callers, state, Target, metadata, !cancelled, token);
        await MessageAsync(callers, state, Sibling,
            new(Sibling, MessageState.Ready, 0, 1L, 1L, null, null), true, token);
        var saved = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(
            new(state.Owner.Partition, QueueLifecyclePublicProtocol.Collection, Witness), token));
        await Assert.That(saved).IsNotNull();
        await Assert.That(saved!.Revision).IsEqualTo(1L);
        await Assert.That(saved.Json).IsEqualTo(QueueLifecyclePublicProtocol.Payload);
    }

    internal static async Task OriginalsAsync(RequestCqrsClusterCallers callers,
        QueueScheduledCancelPublicState state, CancellationToken token)
    {
        await QueueLifecyclePublicRoutes.ReplayAsync(callers, state.Owner, token);
        foreach (var saved in state.Failures)
        {
            var actual = await QueueLeasedCancelPublicAssertions.RefusedAsync(
                callers, state.Owner, saved.Request, saved.Code, token);
            await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(saved.Problem))).IsTrue();
        }
    }

    internal static async Task FailureAsync(RequestCqrsClusterCallers callers, QueueScheduledCancelPublicState state,
        CommandRequest request, ErrorCode code, CancellationToken token)
    {
        var problem = await QueueLeasedCancelPublicAssertions.RefusedAsync(callers, state.Owner, request, code, token);
        state.Failures.Add((request, code, problem));
    }

    internal static async Task TerminalAsync(RequestCqrsClusterCallers callers, QueueScheduledCancelPublicState state,
        string id, long sequence, CancellationToken token)
        => await MessageAsync(callers, state, id,
            new(id, MessageState.Acked, 1, 3L, sequence, null, null, LeaseVersion: 1L), false, token);
}
