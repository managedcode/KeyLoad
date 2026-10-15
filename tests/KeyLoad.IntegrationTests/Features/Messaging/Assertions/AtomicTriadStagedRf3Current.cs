using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicTriadStagedRf3Current
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        AtomicTriadStagedRf3State state, CancellationToken token)
    {
        DocumentResult? document = state.HealthyCommitted ? new(new(state.Partition, AtomicTriadStagedRf3Protocol.Collection,
            AtomicTriadStagedRf3Protocol.Entity), 2L, AtomicTriadStagedRf3Protocol.SecondJson, false, []) : null;
        var request = new GetDocumentRequest(new(state.Partition, AtomicTriadStagedRf3Protocol.Collection, AtomicTriadStagedRf3Protocol.Entity));
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await QueueProducerRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.GetAsync(request.Reference, token)), new DocumentReadResult(document)), failures);
        await ServerFailureObserver.ObserveAsync(async () => await QueueProducerRf3Assertions.EqualAsync(
            (await PublicReadAssertions.DocumentAsync(await reader.Mcp.CallAsync(McpCallerTools.DocumentsGet, request, token))).Value, document), failures);
        await ServerFailureObserver.ObserveAsync(() => StreamAsync(reader, state, token), failures);
        await ServerFailureObserver.ObserveAsync(() => MessageAsync(reader, state, AtomicTriadStagedRf3Protocol.FirstMessage,
            AtomicTriadStagedRf3Protocol.FirstJson, 1L, token), failures);
        await ServerFailureObserver.ObserveAsync(() => MessageAsync(reader, state, AtomicTriadStagedRf3Protocol.SecondMessage,
            AtomicTriadStagedRf3Protocol.SecondJson, 2L, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await QueueProducerRf3Assertions.EqualAsync(
            await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token)), state.Outbox), failures);
        await ServerFailureObserver.ObserveAsync(async () => await QueueProducerRf3Assertions.EqualAsync(
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await root.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Partition), token))).Value, state.Outbox), failures);
        await ServerFailureObserver.ObserveAsync(() => IndexAsync(reader, state, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task StreamAsync(RequestCqrsRf3Callers reader, AtomicTriadStagedRf3State state, CancellationToken token)
    {
        var stream = new StreamRef(state.Partition, AtomicTriadStagedRf3Protocol.Streams, AtomicTriadStagedRf3Protocol.Entity);
        var request = new ReadStreamRequest(stream);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ReadStreamAsync(request, token));
        var official = (await McpCallerAssertions.SuccessAsync<StreamPage>(await reader.Mcp.CallAsync(McpCallerTools.StreamsRead, request, token))).Value;
        EventRecord[] events = state.HealthyCommitted ?
        [new(stream, 1L, 1L, new(AtomicTriadStagedRf3Protocol.FirstEvent, AtomicTriadStagedRf3Protocol.Created, AtomicTriadStagedRf3Protocol.FirstJson, AtomicTriadStagedRf3Protocol.Headers), state.CommittedAt),
         new(stream, 2L, 2L, new(AtomicTriadStagedRf3Protocol.SecondEvent, AtomicTriadStagedRf3Protocol.Replaced, AtomicTriadStagedRf3Protocol.SecondJson, AtomicTriadStagedRf3Protocol.Headers), state.CommittedAt)] : [];
        foreach (var page in new[] { direct, official })
        {
            await QueueProducerRf3Assertions.EqualAsync(page.Head, new StreamHead(state.HealthyCommitted ? 2L : 0L, 1L, 1L));
            await QueueProducerRf3Assertions.EqualAsync(page.Events.ToArray(), events);
            await Assert.That(page.Stream).IsEqualTo(stream);
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Cursor).IsNull();
            await Assert.That(page.CutPosition > 0L).IsTrue();
        }
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers reader, AtomicTriadStagedRf3State state,
        string id, string json, long sequence, CancellationToken token)
    {
        MessageInspection? expected = state.HealthyCommitted ? new(new(id, MessageState.Ready, 0, 1L, sequence, null, null),
            json, AtomicTriadStagedRf3Protocol.Headers) : null;
        var request = new InspectMessageRequest(new(state.Partition, AtomicTriadStagedRf3Protocol.Queue), id);
        await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.InspectAsync(request, token)),
            new MessageInspectionResult(expected));
        await QueueProducerRf3Assertions.EqualAsync((await PublicReadAssertions.MessageAsync(await reader.Mcp.CallAsync(
            McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }

    private static async Task IndexAsync(RequestCqrsRf3Callers reader, AtomicTriadStagedRf3State state, CancellationToken token)
    {
        foreach (var sql in new[] { AtomicTriadStagedRf3Protocol.OldSql, AtomicTriadStagedRf3Protocol.CurrentSql })
        {
            QueryRow[] rows = state.HealthyCommitted && sql == AtomicTriadStagedRf3Protocol.CurrentSql
                ? [new(AtomicTriadStagedRf3Protocol.Entity, 2L, AtomicTriadStagedRf3Protocol.SecondJson, false, [])] : [];
            var request = new QueryRequest(state.Partition, sql);
            var sdk = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token));
            var mcp = (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token))).Value;
            foreach (var page in new[] { sdk, mcp })
            {
                await QueueProducerRf3Assertions.EqualAsync(page.Rows.ToArray(), rows);
                await Assert.That(page.AccessPath).IsEqualTo("index:number");
                await Assert.That(page.Cursor).IsNull();
            }
        }
    }
}
