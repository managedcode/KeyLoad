using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.Search;

internal static class EventProjectionLineageR41Canonical
{
    internal static async Task CaptureEventAsync(RequestCqrsClusterCallers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(new(state.Stream, Limit: 2), token));
        state.OriginalEvent = await Assert.That(page.Events).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(state.Stream, state.OriginalEvent.Stream);
        await Assert.That(state.OriginalEvent.Revision).IsEqualTo(1L);
        await Assert.That(state.OriginalEvent.EventSequence).IsEqualTo(1L);
        await SqlClusterProtocol.EqualAsync(new EventData(EventProjectionLineageR41Protocol.Event,
         EventProjectionLineageR41Protocol.EventType, EventProjectionLineageR41Protocol.EventJson), state.OriginalEvent.Data);
        await Assert.That(state.OriginalEvent.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
    }
    internal static async Task CaptureOutboxAsync(RequestCqrsClusterCallers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Partition, token));
        await Assert.That(state.Outbox.Head.Tail).IsEqualTo(state.Replaced ? 8L : state.Deleted ? 7L : 6L);
    }
    internal static async Task CurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers reader,
     EventProjectionLineageR41State state, CancellationToken token)
    {
        await EventProjectionLineageR41Pages.BothAsync(reader, state, () => ObserveAsync(admin, state, token), token);
    }
    private static async Task ObserveAsync(RequestCqrsClusterCallers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        foreach (var id in new[] { EventProjectionLineageR41Protocol.Source, EventProjectionLineageR41Protocol.Target, EventProjectionLineageR41Protocol.Baseline })
        {
            var reference = new EntityRef(state.Partition, EventProjectionLineageR41Protocol.Collection, id);
            var json = id == EventProjectionLineageR41Protocol.Source ? EventProjectionLineageR41Protocol.SourceJson :
             id == EventProjectionLineageR41Protocol.Target ? EventProjectionLineageR41Protocol.TargetJson : EventProjectionLineageR41Protocol.BaselineJson;
            DocumentResult? expected = state.Deleted && id == EventProjectionLineageR41Protocol.Source ? null : new(reference, 1L, json, false, []);
            await SqlClusterProtocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(await admin.Sdk.GetAsync(reference, token)));
            await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(await admin.Mcp.CallAsync(
             McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
        }
        await EventAsync(admin, state, token);
        await SqlClusterProtocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
         await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Partition), token))).Value);
    }
    private static async Task EventAsync(RequestCqrsClusterCallers admin, EventProjectionLineageR41State state, CancellationToken token)
    {
        var request = new ReadStreamRequest(state.Stream, Limit: 2);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadStreamAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<StreamPage>(await admin.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        foreach (var page in new[] { sdk, mcp })
        {
            await SqlClusterProtocol.EqualAsync(state.Stream, page.Stream);
            await SqlClusterProtocol.EqualAsync(new StreamHead(1L, 1L, 1L), page.Head);
            await SqlClusterProtocol.EqualAsync(new[] { state.OriginalEvent }, page.Events.ToArray());
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.CutPosition).IsGreaterThan(0L);
        }
    }
}
