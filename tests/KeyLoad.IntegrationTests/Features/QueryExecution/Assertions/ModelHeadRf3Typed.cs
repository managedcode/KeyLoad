using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Typed
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, ModelHeadRf3State state, bool queue, CancellationToken token)
    {
        if (queue)
        {
            await SqlRf3Protocol.EqualAsync(new MessageInspection(
                new("ready", MessageState.Ready, 0, 1L, 1L, ModelHeadRf3State.NotBefore, null),
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
        await SqlRf3Protocol.EqualAsync(expected, actual with
        {
            CutPosition = 0L,
            SnapshotCutPosition = 0L
        });
    }
}
