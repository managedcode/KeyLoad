using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapPolicy
{
    internal static async Task ConfigureAsync(MetadataBootstrapState state, long epoch, bool administrator)
    {
        var principal = MetadataBootstrapState.Principal(epoch, administrator);
        await MetadataBootstrapValues.EqualAsync(principal, state.Fixture.Database.Apply(state.Operation(
            OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), "root")).Get<PrincipalRecord>());
    }
    internal static async Task InitializeAsync(MetadataBootstrapState state)
    {
        await ConfigureAsync(state, 1L, true);
        var key = global::KeyLoad.Core.DatabaseEngine.Credential("metadata-bootstrap-key", MetadataBootstrapState.Actor, MetadataBootstrapState.Secret);
        await Assert.That(state.Fixture.Database.Apply(state.Operation(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(key), "root")).Get<bool>()).IsTrue();
    }
    internal static async Task FenceAsync(MetadataBootstrapState state, bool repaired)
    {
        var image = MetadataBootstrapValues.Image(state);
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(state.Fixture.Store);
        var position = state.Fixture.Store.Position;
        var failures = new List<Exception>();
        for (var index = 0; index < state.Originals.Length; index++)
        {
            var original = state.Originals[index];
            var bytes = state.Outcomes[index];
            await ServerFailureObserver.ObserveAsync(async () =>
                await MetadataBootstrapValues.ResultAsync(new(null, ErrorCode.PermissionDenied, repaired
                    ? "The principal policy changed since this command was evaluated." : "Cluster administration is required."),
                    state.Fixture.Database.Apply(original)), failures);
            await ServerFailureObserver.ObserveAsync(async () => await Assert.That(MetadataBootstrapValues.Outcome(state, original)
                .AsSpan().SequenceEqual(bytes)).IsTrue(), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapProducers.CatalogAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapValues.ConservedAsync(state, image, identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
