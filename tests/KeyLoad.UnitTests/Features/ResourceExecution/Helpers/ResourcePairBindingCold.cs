using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourcePairBindingProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourcePairBindingCold
{
    internal static async Task RunAsync(ResourcePairBindingState state, CancellationToken token)
    {
        var identity = state.Source.Store.Identity;
        var image = QueueWholeFlowStorage.Bytes(state.Source.Store);
        var position = state.Source.Store.Position;
        state.ExpectedJournal = state.Source.ReadJournalCut();
        state.Source.JoinOwnedResources();
        for (var wave = Zero; wave < TwoColdWaves; wave += One)
        {
            token.ThrowIfCancellationRequested();
            await ResourcePairBindingAssertions.IdentityAsync(state.ReplicaIdentity, ResourcePairBindingState.ReadReplica(state.Source));
            var result = await WaveAsync(state, identity, image, position, wave, token);
            image = result.Image;
            position = result.Position;
        }
    }

    private static async Task<(string[] Image, long Position)> WaveAsync(ResourcePairBindingState state, StoreIdentity identity,
        string[] image, long position, int wave, CancellationToken token)
    {
        var failures = new List<Exception>();
        var owners = new StorageFailureOwnerLifetime();
        var stores = new StorageFailureOwnerLifetime();
        var store = stores.Acquire(() => ZoneTreeExistingStore.Open(new(state.Source.Directory)
        {
            Incarnation = identity.Incarnation
        },
            identity.NodeId, state.Source.StorageExecution), failures);
        TestDatabaseReplicaAdmission? admission = null;
        var nextImage = image;
        var nextPosition = position;
        if (store is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await ResourcePairBindingAssertions.IdentityAsync(identity, store.Identity);
                await ResourcePairBindingAssertions.UnchangedAsync(store, image, position);
                var database = state.Source.CreateConfiguredDatabase(store);
                admission = owners.Acquire(() => state.Source.CreateTargetAdmission(database, store, state.Source.Directory,
                    state.ReplicaDirectory, state.ReplicaIdentity.NodeId), failures);
                if (admission is null)
                {
                    ServerFailureObserver.ThrowIfAny(failures);
                    throw new InvalidOperationException(Invalid);
                }
                await Assert.That(admission.JournalNodeId).IsEqualTo(state.ReplicaIdentity.NodeId);
                await Assert.That(admission.ReadJournalCut()).IsEqualTo(state.ExpectedJournal);
                await ResourcePairBindingAssertions.UnchangedAsync(store, image, position);
                await RequireHealthyStateAsync(state, store, admission, database, wave, token);
                var request = ResourcePairBindingBatch.Request(state.Source.Partition,
                    ColdPrefix + wave.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var nextIndex = admission.ReadJournalCut().LastIndex + One;
                var result = admission.Submit(ResourcePairBindingBatch.Issue(database, request), explicitTime: false, token);
                var original = admission.ReadRetainedEntry(request.CommandId).Operation!;
                await ResourcePairBindingBatch.AcceptAsync(state, database, store, request, original, result, nextIndex);
                state.Originals.Add((original, result, ResourcePairBindingAssertions.Outcome(store, state.Source.Partition, original)));
                await ResourcePairBindingAssertions.PairsAsync(state, store);
                nextImage = QueueWholeFlowStorage.Bytes(store);
                nextPosition = store.Position;
                state.ExpectedJournal = admission.ReadJournalCut();
            }, failures);
        }
        if (admission is not null)
        {
            owners.Close(admission, failures);
        }
        if (owners.CanAdvance && store is not null)
        {
            stores.Close(store, failures);
        }
        foreach (var failure in failures)
        {
            failure.Data[RootKey] = state.Source.Directory;
            failure.Data[ReplicaRootKey] = state.ReplicaDirectory;
            failure.Data[ColdStoreKey] = store;
            failure.Data[ColdAdmissionKey] = admission;
            failure.Data[ColdLifetimeKey] = (owners, stores);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return (nextImage, nextPosition);
    }

    private static async Task RequireHealthyStateAsync(ResourcePairBindingState state, ZoneTreeStore store,
        TestDatabaseReplicaAdmission admission, DatabaseEngine database, int wave, CancellationToken token)
    {
        await ReplayAsync(state, store, admission);
        await ResourcePairBindingAssertions.ModelsAsync(state, database);
        await ResourcePairBindingBlob.HealthyAsync(state, database);
        await ResourcePairBindingSql.HealthyAsync(state, database, token);
        await ResourcePairBindingQuery.HealthyAsync(state, database);
        await ResourcePairBindingBm25.HealthyAsync(state, database, token, expectedCurrent: wave == Zero);
    }

    private static async Task ReplayAsync(ResourcePairBindingState state, ZoneTreeStore store,
        TestDatabaseReplicaAdmission admission)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var journal = admission.ReadJournalCut();
        foreach (var value in state.Originals)
        {
            await ResourceIncarnationAssertions.ResultAsync(value.Result, admission.Submit(value.Original, explicitTime: true));
            await Assert.That(ResourcePairBindingAssertions.Outcome(store, state.Source.Partition, value.Original)
                .AsSpan().SequenceEqual(value.Outcome)).IsTrue();
        }
        await ResourcePairBindingAssertions.UnchangedAsync(store, image, position);
        await Assert.That(admission.ReadJournalCut()).IsEqualTo(journal);
        await ResourcePairBindingAssertions.PairsAsync(state, store);
    }
}
