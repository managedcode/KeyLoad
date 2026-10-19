using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class ModelHeadClusterCurrent
{
    internal static async Task SourceAsync(StreamPage page, StreamRef source)
    {
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        await SqlClusterProtocol.EqualAsync(source, page.Stream);
        await SqlClusterProtocol.EqualAsync(new StreamHead(2L, 1L, 1L), page.Head);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Events.Length).IsEqualTo(2);
        var expected = new[] { ("created", "Created", "first", 1L), ("paid", "Paid", "second", 2L) };
        foreach (var (record, golden) in page.Events.Zip(expected))
        {
            await SqlClusterProtocol.EqualAsync(source, record.Stream);
            await Assert.That(record.Revision).IsEqualTo(golden.Item4);
            await Assert.That(record.EventSequence).IsEqualTo(golden.Item4);
            await SqlClusterProtocol.EqualAsync(new EventData(golden.Item1, golden.Item2, ModelHeadClusterState.Payload(golden.Item3),
                ModelHeadClusterState.Headers), record.Data);
        }
    }
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, ModelHeadClusterState state, Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(new(state.Source, Limit: 10), token));
            await SourceAsync(page, state.Source);
            await SqlClusterProtocol.EqualAsync(state.Original, page with
            {
                CutPosition = 0L,
                SnapshotCutPosition = 0L
            });
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(
            new MessageInspection(new("ready", MessageState.Ready, 0, 1L, 1L, ModelHeadClusterState.NotBefore, null),
                ModelHeadClusterState.Payload("queue"), ModelHeadClusterState.Headers),
            await PublicReadAssertions.MessageAsync(await root.Sdk.InspectAsync(new(state.Lane, "ready"), token))), failures);
        await ServerFailureObserver.ObserveAsync(() => QueueAsync(root, state, token), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SqlClusterProtocol.EqualAsync(state.Outbox,
            await QueryCursorRequestIdentityR47Current.OutboxAsync(root, state.Partition, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task QueueAsync(RequestCqrsClusterCallers root, ModelHeadClusterState state, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(ModelHeadClusterRequests.Ast(state, true), token));
        var json = JsonSerializer.Serialize(new
        {
            id = "ready",
            state = "Ready",
            attempts = 0,
            stateVersion = 1L,
            notBefore = ModelHeadClusterState.NotBefore,
            expiresAt = (DateTimeOffset?)null,
            payload = JsonSerializer.Deserialize<JsonElement>(ModelHeadClusterState.Payload("queue")),
            headers = JsonSerializer.Deserialize<JsonElement>(ModelHeadClusterState.Headers)
        }, JsonDefaults.Options);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
        await SqlClusterProtocol.EqualAsync(new QueryPage([new("ready", 1L, json, false, [])], null, actual.CutPosition,
            ModelHeadClusterRequests.Path(true)), actual);
    }
    internal static async Task ReplayAsync(RequestCqrsClusterCallers root, ModelHeadClusterState state, CancellationToken token)
        => await ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.Receipt, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(state.Seed, token)));
            await SqlClusterProtocol.EqualAsync(state.Receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        }, token);
}
