using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterImages
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
        Func<Task> operation, CancellationToken token)
    {
        var before = await CaptureAsync(root, history, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(StringComparer.Ordinal.Equals(await CaptureAsync(root, history, token), before)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task MutationAsync(RequestCqrsClusterCallers root, SafeProjectorOperationClusterHistory history,
        Func<Task> operation, CancellationToken token)
    {
        var before = await CaptureAsync(root, history, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(StringComparer.Ordinal.Equals(await CaptureAsync(root, history, token), before)).IsTrue();
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task<string> CaptureAsync(RequestCqrsClusterCallers root,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        var state = history.State;
        var first = await PublicReadAssertions.DocumentAsync(await root.Sdk.GetAsync(state.First, token))
            ?? throw new InvalidOperationException(SafeProjectorOperationClusterProtocol.MissingDocument);
        var second = await PublicReadAssertions.DocumentAsync(await root.Sdk.GetAsync(state.Second, token))
            ?? throw new InvalidOperationException(SafeProjectorOperationClusterProtocol.MissingDocument);
        await SqlClusterProtocol.EqualAsync(new DocumentResult(state.First, history.Revision, history.RawJson, false, []), first);
        await SqlClusterProtocol.EqualAsync(new DocumentResult(state.Second, SafeProjectorOperationClusterProtocol.InitialRevision,
            SafeProjectorOperationClusterProtocol.SecondJson, false, []), second);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Partition, token));
        var tail = history.HealthyPatch is not null ? HealthySequence : history.PublicPatch is not null ? PublicSequence : SeedSequence;
        await Assert.That(outbox.Head.Tail).IsEqualTo(tail);
        await Assert.That(outbox.Head.FirstAvailable).IsEqualTo(FirstSequence);
        await Assert.That(outbox.Head.StoredRecords).IsEqualTo(tail);
        await Assert.That(outbox.Head.StoredBytes).IsGreaterThan(NoStoredBytes);
        await Assert.That(outbox.Consumers).IsEmpty();
        return JsonSerializer.Serialize(new
        {
            First = first,
            Second = second,
            Outbox = outbox
        }, JsonDefaults.Options);
    }

    private const long FirstSequence = 1;
    private const long SeedSequence = 2;
    private const long PublicSequence = 3;
    private const long HealthySequence = 4;
    private const long NoStoredBytes = 0;
}
