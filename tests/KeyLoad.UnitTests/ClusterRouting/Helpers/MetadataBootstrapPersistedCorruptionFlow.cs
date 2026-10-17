using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapPersistedCorruptionFlow
{
    internal static async Task RunAsync(string variant, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var state = new MetadataBootstrapState(false);
            await ServerFailureObserver.ObserveAsync(() => OwnedAsync(state, variant, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OwnedAsync(MetadataBootstrapState state, string variant, CancellationToken token)
    {
        await MetadataBootstrapPolicy.InitializeAsync(state);
        await MetadataBootstrapProducers.RunAsync(state, token);
        await MetadataBootstrapPolicy.ConfigureAsync(state, 2L, false);
        await MetadataBootstrapPolicy.FenceAsync(state, false);
        await MetadataBootstrapPolicy.ConfigureAsync(state, 3L, true);
        await MetadataBootstrapContinuation.HealthyAsync(state, token);
        var originalCatalog = state.Fixture.ReadCatalogBytes();
        var originalPosition = state.Position;
        var expectedMessage = variant == "empty"
            ? "The internal binary payload is invalid."
            : "The committed physical shard catalog is malformed.";
        Corrupt(state, variant);
        var corruptImage = MetadataBootstrapValues.Image(state);
        var attempt = state.Operation(OperationKind.BootstrapPhysicalShardCatalog, state.Requests[state.Winner]);
        await MetadataBootstrapPersistedCorruptionAssertions.RefusedAsync(state, attempt, corruptImage, originalPosition + 1L, expectedMessage);
        state.Fixture.Reopen();
        await MetadataBootstrapPersistedCorruptionAssertions.RefusedAsync(state, attempt, corruptImage, originalPosition + 1L, expectedMessage);
        state.Fixture.ReplaceCatalogBytes(originalCatalog);
        OperationResult expected = new(null)
        {
            NativeValue = true
        };
        await MetadataBootstrapValues.ResultAsync(expected, state.Fixture.Database.Apply(attempt));
        var outcome = MetadataBootstrapValues.Outcome(state, attempt);
        await MetadataBootstrapValues.StoredAsync(state, attempt, expected, outcome, 3L);
        await MetadataBootstrapPersistedCorruptionAssertions.OriginalsAsync(state);
        state.Image = MetadataBootstrapValues.Image(state);
        state.Position = originalPosition + 3L;
        await MetadataBootstrapContinuation.RequireAsync(state, token);
        var failures = new List<Exception>();
        for (var cold = 0; cold < 2; cold++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Fixture.Reopen();
                await MetadataBootstrapContinuation.RequireAsync(state, token);
                await MetadataBootstrapValues.ResultAsync(expected, state.Fixture.Database.Apply(attempt));
                await Assert.That(MetadataBootstrapValues.Outcome(state, attempt).AsSpan().SequenceEqual(outcome)).IsTrue();
                await MetadataBootstrapValues.ConservedAsync(state, state.Image, state.Identity, state.Position);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Corrupt(MetadataBootstrapState state, string variant)
    {
        if (variant == "version")
        {
            state.Fixture.ReplaceCatalog(new PhysicalShardCatalog(2, 1L, new(
                state.Requests[state.Winner].PhysicalShardId, state.Fixture.Store.Identity.Incarnation,
                PhysicalShardCatalogVoterIds.Standard, 1L)));
            return;
        }
        state.Fixture.ReplaceCatalogBytes(variant == "empty" ? [] : new byte[8_193]);
    }
}
