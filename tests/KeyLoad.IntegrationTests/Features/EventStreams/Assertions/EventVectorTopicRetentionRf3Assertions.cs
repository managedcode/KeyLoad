using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventVectorTopicRetentionRf3Assertions
{
    internal static Task<EventSourcePage> SourceAsync(TwoRf3MembershipWave wave, string credential,
        EventVectorTopicRetentionRf3Seed seed, bool purged, bool healthy, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, seed.Node, credential, async callers =>
        {
            var request = new ReadEventSourceRequest(seed.Source, AfterPosition: purged ? 2 : 0);
            var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(request, token));
            var official = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await callers.Mcp.CallAsync(
                McpCallerTools.EventsRead, request, token))).Value;
            await SqlRf3Protocol.EqualAsync(page.Source, official.Source);
            await SqlRf3Protocol.EqualAsync(page.Head, official.Head);
            await SqlRf3Protocol.EqualAsync(page.Events, official.Events);
            await Assert.That(page.HasMore || official.HasMore).IsFalse();
            await SqlRf3Protocol.EqualAsync(seed.Source, page.Source);
            await SqlRf3Protocol.EqualAsync(new EventSourceHead(healthy ? 4 : 3, purged ? 3 : 1, 1), page.Head);
            var expected = purged ? EventVectorTopicRetentionRf3Seed.Events()[2..]
                : EventVectorTopicRetentionRf3Seed.Events();
            if (healthy)
            { expected = [.. expected, new("fourth", "Created", "{\"n\":4}")]; }
            await Assert.That(page.Events.Length).IsEqualTo(expected.Length);
            for (var index = 0; index < expected.Length; index++)
            {
                var position = (long)index + (purged ? 3 : 1);
                await Assert.That(page.Events[index].Position).IsEqualTo(position);
                await Assert.That(page.Events[index].EventSequence).IsEqualTo(position);
                await SqlRf3Protocol.EqualAsync(expected[index], page.Events[index].Data);
            }
            await UnrelatedAsync(callers, seed, token);
            if (purged)
            { await HistoryAsync(callers, seed.Source, token); }
            return page;
        }, token);

    private static async Task UnrelatedAsync(RequestCqrsRf3Callers callers,
        EventVectorTopicRetentionRf3Seed seed, CancellationToken token)
    {
        var reference = seed.Document.Reference;
        var document = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(reference, token));
        var officialDocument = (await PublicReadAssertions.RequiredDocumentAsync(await callers.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value;
        await SqlRf3Protocol.EqualAsync(seed.Document, document);
        await SqlRf3Protocol.EqualAsync(seed.Document, officialDocument);
        var request = new InspectMessageRequest(new(seed.Source.Partition, EventVectorTopicRetentionRf3Seed.Queue),
            EventVectorTopicRetentionRf3Seed.Item);
        var message = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token));
        var officialMessage = (await PublicReadAssertions.RequiredMessageAsync(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value;
        await SqlRf3Protocol.EqualAsync(seed.Message, message);
        await SqlRf3Protocol.EqualAsync(seed.Message, officialMessage);
    }

    private static async Task HistoryAsync(RequestCqrsRf3Callers callers, EventSourceRef source, CancellationToken token)
    {
        var request = new ReadEventSourceRequest(source);
        var sdk = await callers.Sdk.ReadEventSourceAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(ErrorCode.HistoryUnavailable.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.EventsRead,
            request, token), ErrorCode.HistoryUnavailable, dispatched: true);
    }
}
