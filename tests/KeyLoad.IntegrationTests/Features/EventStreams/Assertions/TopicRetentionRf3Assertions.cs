using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class TopicRetentionRf3Assertions
{
    internal static async Task RetainedAsync(TopicRetentionRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient session, CancellationToken token)
    {
        var request = new ReadEventSourceRequest(scenario.Source, AfterPosition: 2);
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(request, token));
        var mcpPage = await McpCallerAssertions.SuccessAsync<EventSourcePage>(await session.CallAsync(McpCallerTools.EventsRead, request, token));
        await Assert.That(sdkPage.Head).IsEqualTo(new EventSourceHead(3, 3, 1));
        await Assert.That(mcpPage.Value.Head).IsEqualTo(sdkPage.Head);
        await Assert.That(JsonDefaults.Serialize(mcpPage.Value.Events).AsSpan().SequenceEqual(JsonDefaults.Serialize(sdkPage.Events))).IsTrue();
        var record = sdkPage.Events.Single();
        await Assert.That(record.Source).IsEqualTo(scenario.Source);
        await Assert.That(record.Position).IsEqualTo(3L);
        await Assert.That(record.EventSequence).IsEqualTo(3L);
        await Assert.That(record.Data).IsEqualTo(new EventData("event3", "Created", "{\"n\":3}"));
        await Assert.That(record.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        var lost = await sdk.ReadEventSourceAsync(new(scenario.Source), token);
        await Assert.That(lost.IsSuccess).IsFalse();
        await Assert.That(lost.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.HistoryUnavailable));
        await McpCallerAssertions.ErrorAsync(await session.CallAsync(McpCallerTools.EventsRead,
            new ReadEventSourceRequest(scenario.Source), token), ErrorCode.HistoryUnavailable, dispatched: true);
        var group = await McpCallerAssertions.SdkSuccessAsync(await sdk.SubscriptionStatusAsync(scenario.Subscription, token));
        await Assert.That(group.Checkpoint).IsEqualTo(3L);
        await Assert.That(group.Generation).IsEqualTo(2L);
        await Assert.That(group.Paused).IsTrue();
        await PreservedModelsAsync(scenario, sdk, session, token);
    }

    private static async Task PreservedModelsAsync(TopicRetentionRf3Scenario scenario, KeyLoadClient sdk,
        McpOfficialClient session, CancellationToken token)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(new(scenario.Partition, "orders", "derived"), token));
        await Assert.That(document!.Json).IsEqualTo("{\"done\":true}");
        await Assert.That(document.Revision).IsEqualTo(1L);
        var request = new InspectMessageRequest(new(scenario.Partition, "work"), "derived");
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await session.CallAsync(
            McpCallerTools.MessagesInspect, request, token));
        await Assert.That(message!.PayloadJson).IsEqualTo("{\"input\":1}");
        await Assert.That(message.HeadersJson).IsEqualTo("{}");
        await Assert.That(message.Metadata).IsEqualTo(new MessageMetadata("derived", MessageState.Ready, 0, 1, 1, null, null));
        await Assert.That(JsonDefaults.Serialize(official.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(message))).IsTrue();
    }
}
