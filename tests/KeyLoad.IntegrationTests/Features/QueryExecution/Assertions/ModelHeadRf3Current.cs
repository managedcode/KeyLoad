using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadRf3Current
{
    internal static async Task SourceAsync(StreamPage page, StreamRef source)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        await SqlRf3Protocol.EqualAsync(source, page.Stream);
        await SqlRf3Protocol.EqualAsync(new StreamHead(2L, 1L, 1L), page.Head);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Events.Length).IsEqualTo(2);
        var expected = new[] { ("created", "Created", "first", 1L), ("paid", "Paid", "second", 2L) };
        foreach (var (record, golden) in page.Events.Zip(expected))
        {
            await SqlRf3Protocol.EqualAsync(source, record.Stream);
            await Assert.That(record.Revision).IsEqualTo(golden.Item4);
            await Assert.That(record.EventSequence).IsEqualTo(golden.Item4);
            await SqlRf3Protocol.EqualAsync(new EventData(golden.Item1, golden.Item2, ModelHeadRf3State.Payload(golden.Item3),
                ModelHeadRf3State.Headers), record.Data);
        }
    }
    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, ModelHeadRf3State state, Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(state.Source, Limit: 10), token));
            await SourceAsync(page, state.Source);
            await SqlRf3Protocol.EqualAsync(state.Original, page with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            });
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(
            new MessageInspection(new("ready", MessageState.Ready, 0, 1L, 1L, ModelHeadRf3State.NotBefore, null),
                ModelHeadRf3State.Payload("queue"), ModelHeadRf3State.Headers),
            await PublicReadAssertions.MessageAsync(await root.Sdk.InspectAsync(new(state.Lane, "ready"), token))), failures);
        await ServerFailureObserver.ObserveAsync(() => QueueAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task QueueAsync(RequestCqrsRf3Callers root, ModelHeadRf3State state, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(ModelHeadRf3Requests.Ast(state, true), token));
        var json = JsonSerializer.Serialize(new
        {
            id = "ready",
            state = "Ready",
            attempts = 0,
            stateVersion = 1L,
            notBefore = ModelHeadRf3State.NotBefore,
            expiresAt = (DateTimeOffset?)null,
            payload = JsonSerializer.Deserialize<JsonElement>(ModelHeadRf3State.Payload("queue")),
            headers = JsonSerializer.Deserialize<JsonElement>(ModelHeadRf3State.Headers)
        }, JsonDefaults.Options);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(new QueryPage([new("ready", 1L, json, false, [])], null, actual.CutPosition,
            ModelHeadRf3Requests.Path(true)), actual);
    }
    internal static async Task ReplayAsync(RequestCqrsRf3Callers root, ModelHeadRf3State state, CancellationToken token)
        => await ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
            await SqlRf3Protocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
}
