using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ConnectionQueryContextCurrent
{
    internal static async Task ObserveAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        Func<Task> operation, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        foreach (var seed in new[] { state.First, state.Second })
        {
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(scenario, seed, token), failures);
            await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(seed.Receipt,
                await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.CommitAsync(seed.Command, token))), failures);
            await ServerFailureObserver.ObserveAsync(() => ImageAsync(scenario, seed, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task<T> CaptureAsync<T>(ConnectionRf3Scenario scenario, ConnectionQueryContextState state,
        Func<Task<T>> operation, CancellationToken token)
    {
        var captured = new List<T>();
        await ObserveAsync(scenario, state, async () => captured.Add(await operation()), token);
        return captured.Single();
    }
    private static async Task ImageAsync(ConnectionRf3Scenario scenario, ConnectionQueryContextSeed seed, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var (id, json) in new[] { (ConnectionQueryContextRequests.AddedId, seed.AddedJson),
            (RequestCqrsRf3Protocol.DocumentId, RequestCqrsRf3Protocol.DocumentJson) })
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var reference = new EntityRef(seed.Identity.Partition, RequestCqrsRf3Protocol.AdminCollection, id);
                await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, 1L, json, false, []),
                    await PublicReadAssertions.DocumentAsync(await scenario.Administrator.GetAsync(reference, seed.Receipt.Token, token)));
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => await SqlRf3Protocol.EqualAsync(seed.Outbox,
            await McpCallerAssertions.SdkSuccessAsync(await scenario.Administrator.OutboxStatusAsync(seed.Identity.Partition, token))), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task PageAsync(QueryPage actual, ConnectionQueryContextSeed seed, bool first,
        string? expectedCursor, long? cut = null)
    {
        if (first)
        {
            await Assert.That(actual.Cursor).IsNotNull();
        }
        await SqlRf3Protocol.EqualAsync(new QueryPage([.. ConnectionQueryContextRequests.Rows(seed, first)],
            first ? actual.Cursor : expectedCursor, cut ?? actual.CutPosition, ConnectionQueryContextRequests.Path), actual);
        await Assert.That(actual.CutPosition).IsGreaterThan(0L);
    }
}
