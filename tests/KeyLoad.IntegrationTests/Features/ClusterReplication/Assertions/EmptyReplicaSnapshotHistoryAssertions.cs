using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterReplicationTestSupport;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.EmptyReplicaSnapshotAssertions;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.RetainedReplicaSnapshotScenario;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class EmptyReplicaSnapshotHistoryAssertions
{
    private const string Collection = "snapshots";

    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, SnapshotState state,
        EmptyReplicaSnapshotBaseline actual, EmptyReplicaSnapshotBaseline expected, CancellationToken token)
    {
        var events = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await mcp.CallAsync(
            McpCallerTools.EventsRead, new ReadEventSourceRequest(state.Subscription.Source), token))).Value;
        var changes = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await mcp.CallAsync(
            McpCallerTools.ChangesRead, new ReadChangeFeedRequest(state.Partition, Collection), token))).Value;
        await EventFieldsAsync(actual.Events, expected.Events);
        await EventFieldsAsync(events, expected.Events);
        await ChangeFieldsAsync(actual.Changes, expected.Changes);
        await ChangeFieldsAsync(changes, expected.Changes);
        var nextEvents = Success(await sdk.ReadEventSourceAsync(new(state.Subscription.Source, Cursor: events.Cursor), token));
        await Assert.That(nextEvents.Events).IsEmpty();
        await Assert.That(nextEvents.HasMore).IsFalse();
        await ExactAsync(nextEvents.Head, expected.Events.Head);
        var nextChanges = (await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await mcp.CallAsync(
            McpCallerTools.ChangesRead, new ReadChangeFeedRequest(state.Partition, Collection, changes.Cursor), token))).Value;
        await Assert.That(nextChanges.Changes).IsEmpty();
        await Assert.That(nextChanges.HasMore).IsFalse();
        await Assert.That(nextChanges.ThroughSequence).IsEqualTo(expected.Changes.ThroughSequence);
    }

    private static async Task EventFieldsAsync(EventSourcePage actual, EventSourcePage expected)
    {
        await ExactAsync(new { actual.Source, actual.Head, actual.Events, actual.HasMore },
            new { expected.Source, expected.Head, expected.Events, expected.HasMore });
        await Assert.That(actual.Cursor).IsNotNullOrEmpty();
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
    }

    private static async Task ChangeFieldsAsync(ChangeFeedPage actual, ChangeFeedPage expected)
    {
        await ExactAsync(new { actual.Changes, actual.ThroughSequence, actual.Tail, actual.FirstAvailable, actual.HasMore },
            new { expected.Changes, expected.ThroughSequence, expected.Tail, expected.FirstAvailable, expected.HasMore });
        await Assert.That(actual.Cursor).IsNotNullOrEmpty();
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(expected.CutPosition);
    }
}
