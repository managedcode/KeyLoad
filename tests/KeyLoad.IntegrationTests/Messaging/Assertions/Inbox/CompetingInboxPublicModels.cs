using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class CompetingInboxPublicModels
{
    internal static async Task RequireAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ItemAsync(state, root, CompetingInboxPublicProtocol.First,
            CompetingInboxPublicProtocol.Effect, state.Json(state.WinnerIndex), 1L, token), failures);
        if (state.HasHealthy)
        {
            await ServerFailureObserver.ObserveAsync(() => ItemAsync(state, root, CompetingInboxPublicProtocol.Next,
                CompetingInboxPublicProtocol.Healthy, CompetingInboxPublicProtocol.HealthyJson, 2L, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => CompetingInboxPublicInventory.RunAsync(state, root, token), failures);
        await ServerFailureObserver.ObserveAsync(() => OutboxAsync(state, root,
            state.HasHealthy ? CompetingInboxPublicProtocol.FinalOutboxRecords : CompetingInboxPublicProtocol.FirstOutboxRecords, false, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ItemAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        string input, string effect, string json, long sequence, CancellationToken token)
    {
        var failures = new List<Exception>();
        var reference = new EntityRef(state.Partition, CompetingInboxPublicProtocol.Documents, effect);
        var expected = new DocumentReadResult(new(reference, 1L, json, false, []));
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.GetAsync(reference, state.Winner.Token, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<DocumentReadResult>(await root.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(reference, state.Winner.Token), token))).Value), failures);
        var inputImage = new MessageInspectionResult(new(new(input, MessageState.Acked, 1, 3L, sequence,
            null, null, null, 1L), null, null));
        var inputRequest = new InspectMessageRequest(state.Lane, input);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(inputImage,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(inputRequest, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(inputImage,
            (await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(await root.Mcp.CallAsync(McpCallerTools.MessagesInspect,
                inputRequest, token))).Value), failures);
        var outputImage = new MessageInspectionResult(new(new(effect, MessageState.Ready, 0, 1L, sequence,
            null, null), json, CompetingInboxPublicProtocol.EmptyJson));
        var output = new InspectMessageRequest(new(state.Partition, CompetingInboxPublicProtocol.Output), effect);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(outputImage,
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.InspectAsync(output, token))), failures);
        await ServerFailureObserver.ObserveAsync(async () => await CompetingInboxPublicValues.EqualAsync(outputImage,
            (await McpCallerAssertions.SuccessAsync<MessageInspectionResult>(await root.Mcp.CallAsync(McpCallerTools.MessagesInspect,
                output, token))).Value), failures);
        await ServerFailureObserver.ObserveAsync(() => StreamAsync(state, root, effect, json, sequence, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task StreamAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        string id, string json, long sequence, CancellationToken token)
    {
        var reference = new StreamRef(state.Partition, CompetingInboxPublicProtocol.Streams, id);
        var request = new ReadStreamRequest(reference);
        var failures = new List<Exception>();
        foreach (var official in new[]
        {
            false,
            true
        })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var page = official
                    ? (await McpCallerAssertions.SuccessAsync<StreamPage>(await root.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(request, token));
                state.Events.TryAdd(id, page.Events.ToArray());
                await Assert.That(page.CutPosition).IsGreaterThan(0L);
                await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
                await Assert.That(page.HasMore).IsFalse();
                await Assert.That(page.Cursor).IsNull();
                await CompetingInboxPublicValues.EqualAsync(reference, page.Stream);
                await CompetingInboxPublicValues.EqualAsync(new StreamHead(1L, 1L, 1L), page.Head);
                var record = await Assert.That(page.Events).HasSingleItem();
                await Assert.That(record.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
                await Assert.That(record.RecordedAt.Offset).IsEqualTo(TimeSpan.Zero);
                await Assert.That(record.RecordedAt).IsGreaterThanOrEqualTo(state.EventWindows[id].Before);
                await Assert.That(record.RecordedAt).IsLessThanOrEqualTo(state.EventWindows[id].After);
                await CompetingInboxPublicValues.EqualAsync(new EventRecord(reference, 1L, sequence,
                    new(id, CompetingInboxPublicProtocol.EventType, json), record.RecordedAt), record);
                await CompetingInboxPublicValues.EqualAsync(state.Events[id], page.Events.ToArray());
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task OutboxAsync(CompetingInboxPublicState state, RequestCqrsClusterCallers root,
        long records, bool capture, CancellationToken token)
    {
        var status = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        await Assert.That(status.Head.Tail).IsEqualTo(records);
        await Assert.That(status.Head.StoredRecords).IsEqualTo(records);
        await Assert.That(status.Head.FirstAvailable).IsEqualTo(1L);
        await Assert.That(status.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(status.Consumers.IsEmpty).IsTrue();
        if (capture)
        {
            state.Outboxes.Add(records, status);
        }
        else
        {
            await CompetingInboxPublicValues.EqualAsync(state.Outboxes[records], status);
        }
    }
}
