using System.Text.Json;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class RestoreIdentityOverrideCold
{
    internal static async Task<RestoreIdentityOverrideCurrent> WarmAsync(RestoreIdentityOverrideFixture fixture,
        RestoreIdentityOverrideState state, StoreIdentity identity, CancellationToken token)
    {
        RestoreIdentityOverrideCurrent? saved = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var store = Open(fixture);
            var database = BackupIndexedDocumentDedupFixture.CreateDatabase(store);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(store.Position).IsEqualTo(state.SourceCut + 1L);
                await RestoreIdentityOverrideAssertions.ContentAsync(fixture, database, false);
                var fenced = await RestoreIdentityOverrideAssertions.FenceAsync(fixture, state, database, store);
                var operation = fixture.Batch(database, RestoreIdentityOverrideState.HealthyCommandId,
                    new PutDocument(RestoreIdentityOverrideState.Collection, RestoreIdentityOverrideState.HealthyId,
                        RestoreIdentityOverrideState.HealthyJson, 0));
                var result = database.Apply(operation);
                await RestoreIdentityOverrideAssertions.ReceiptAsync(result, RestoreIdentityOverrideState.HealthyCommandId,
                    RestoreIdentityOverrideState.HealthyId);
                saved = new(operation, result, fenced, BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(store,
                    fixture.Partition, RestoreIdentityOverrideState.Principal, RestoreIdentityOverrideState.HealthyCommandId),
                    store.Position, QueueWholeFlowStorage.Bytes(store));
                await RestoreIdentityOverrideAssertions.ContentAsync(fixture, database, true);
                await RestoreIdentityOverrideAssertions.ReplayAsync(fixture, database, store, saved);
                await IdentityAsync(store, identity);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return saved ?? throw new InvalidOperationException("The restored healthy operation did not complete.");
    }

    internal static async Task ReopenAsync(RestoreIdentityOverrideFixture fixture, RestoreIdentityOverrideState state,
        StoreIdentity identity, RestoreIdentityOverrideCurrent saved, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            using var store = Open(fixture);
            var database = BackupIndexedDocumentDedupFixture.CreateDatabase(store);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(store.Position).IsEqualTo(saved.Position);
                await Assert.That(QueueWholeFlowStorage.Bytes(store))
                    .IsEquivalentTo(saved.Image, CollectionOrdering.Matching);
                await IdentityAsync(store, identity);
                await RestoreIdentityOverrideAssertions.ContentAsync(fixture, database, true);
                var fenced = await RestoreIdentityOverrideAssertions.FenceAsync(fixture, state, database, store);
                await Assert.That(fenced).IsEqualTo(saved.Fence);
                await RestoreIdentityOverrideAssertions.ReplayAsync(fixture, database, store, saved);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static ZoneTreeStore Open(RestoreIdentityOverrideFixture fixture)
        => new(new ZoneTreeStoreOptions(fixture.TargetDirectory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution(), timeProvider: fixture.Source.Database.EvaluationClock);

    private static async Task IdentityAsync(ZoneTreeStore store, StoreIdentity expected)
    {
        await Assert.That(JsonSerializer.Serialize(store.Identity, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        await Assert.That(store.Identity.NodeId).IsEqualTo(expected.NodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(expected.Incarnation);
        await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(expected.SigningKey.Span)).IsTrue();
        await Assert.That(store.Identity.DispatchPaused).IsTrue();
    }
}
