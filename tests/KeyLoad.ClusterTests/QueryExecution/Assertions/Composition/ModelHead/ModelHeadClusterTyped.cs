using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class ModelHeadClusterTyped
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, ModelHeadClusterState state, bool queue, CancellationToken token)
    {
        if (queue)
        {
            await SqlClusterProtocol.EqualAsync(new MessageInspection(
                new("ready", MessageState.Ready, 0, 1L, 1L, ModelHeadClusterState.NotBefore, null),
                "{\"visible\":\"queue\"}", "{\"trace\":\"source\"}"),
                await PublicReadAssertions.MessageAsync(await reader.Sdk.InspectAsync(new(state.Lane, "ready"), token)));
            return;
        }
        var original = state.Original;
        var expected = original with
        {
            Events = [new(state.Source, 1L, 1L, new("created", "Created", "{\"visible\":\"first\"}", "{\"trace\":\"source\"}"), original.Events[0].RecordedAt),
                new(state.Source, 2L, 2L, new("paid", "Paid", "{\"visible\":\"second\"}", "{\"trace\":\"source\"}"), original.Events[1].RecordedAt)]
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(new(state.Source, Limit: 10), token));
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
        await SqlClusterProtocol.EqualAsync(expected, actual with
        {
            CutPosition = 0L,
            SnapshotCutPosition = 0L
        });
    }
}
