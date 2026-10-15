using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapProducers
{
    internal static async Task RunAsync(MetadataBootstrapState state, CancellationToken token)
    {
        var position = state.Fixture.Store.Position;
        state.Identity = global::KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.SnapshotIdentity(state.Fixture.Store);
        var time = state.Fixture.Database.EvaluationClock.GetUtcNow();
        state.Originals = state.Requests.Select(request => state.Operation(OperationKind.BootstrapPhysicalShardCatalog, request, time: time)).ToArray();
        var ready = state.Originals.Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = state.Originals.Select((operation, index) => Task.Run(async () =>
        {
            ready[index].TrySetResult();
            await release.Task.WaitAsync(MetadataBootstrapState.Deadline, TimeProvider.System, token);
            return state.Fixture.Database.Apply(operation);
        })).ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(ready.Select(boundary => boundary.Task))
            .WaitAsync(MetadataBootstrapState.Deadline, TimeProvider.System, token), failures);
        ServerFailureObserver.Observe(() => release.TrySetResult(), failures);
        foreach (var task in tasks)
        {
            await ServerFailureObserver.ObserveAsync(async () => { await task; }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        state.Results = await Task.WhenAll(tasks);
        var successes = Enumerable.Range(0, state.Results.Length).Where(index => state.Results[index].Error is null).ToArray();
        await Assert.That(successes.Length).IsEqualTo(state.Conflicting ? 1 : 2);
        state.Winner = successes[0];
        state.Outcomes = state.Originals.Select(operation => MetadataBootstrapValues.Outcome(state, operation)).ToArray();
        for (var index = 0; index < state.Results.Length; index++)
        {
            var expected = index == state.Winner || !state.Conflicting ? new OperationResult(null) { NativeValue = true }
                : new OperationResult(null, ErrorCode.Conflict, "The physical shard catalog conflicts with configured cluster identity.");
            await MetadataBootstrapValues.ResultAsync(expected, state.Results[index]);
            await MetadataBootstrapValues.StoredAsync(state, state.Originals[index], expected, state.Outcomes[index]);
        }
        await CatalogAsync(state);
        await global::KeyLoad.UnitTests.Features.StorageRecovery.Assertions.NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            state.Fixture.Store, state.Identity, position + 2L);
    }
    internal static Task CatalogAsync(MetadataBootstrapState state)
    {
        var expected = state.Requests[state.Winner];
        return MetadataBootstrapValues.EqualAsync(new PhysicalShardCatalog(1, 1L,
            new(expected.PhysicalShardId, expected.Incarnation, expected.VoterIds, 1L)),
            state.Fixture.Database.ReadPhysicalShardCatalog("root"));
    }
}
