using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapContinuation
{
    internal static async Task HealthyAsync(MetadataBootstrapState state, CancellationToken token)
    {
        state.Fresh = state.Operation(OperationKind.BootstrapPhysicalShardCatalog, state.Requests[state.Winner]);
        await MetadataBootstrapValues.ResultAsync(new(null)
        {
            NativeValue = true
        }, state.Fixture.Database.Apply(state.Fresh));
        state.FreshOutcome = MetadataBootstrapValues.Outcome(state, state.Fresh);
        await MetadataBootstrapValues.StoredAsync(state, state.Fresh, new(null)
        {
            NativeValue = true
        }, state.FreshOutcome, 3L);
        foreach (var (name, kind) in new[] { (MetadataBootstrapState.Collection, ResourceKind.Collection),
            (MetadataBootstrapState.Streams, ResourceKind.StreamSet), (MetadataBootstrapState.Queue, ResourceKind.WorkQueue) })
        {
            var resource = new ResourceDefinition(name, kind, state.Partition.TransactionDomainId);
            await MetadataBootstrapValues.EqualAsync(resource, state.Fixture.Database.Apply(state.Operation(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, resource), "root")).Get<ResourceDefinition>());
        }
        var id = Guid.NewGuid();
        state.Request = new(id, state.Partition,
            [new PutDocument(MetadataBootstrapState.Collection, MetadataBootstrapState.Entity, MetadataBootstrapState.Json, 0L),
             new AppendEvents(MetadataBootstrapState.Streams, MetadataBootstrapState.Entity,
                [new(MetadataBootstrapState.Entity, "Created", MetadataBootstrapState.Json)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(MetadataBootstrapState.Queue, MetadataBootstrapState.Entity, MetadataBootstrapState.Json)]);
        state.Command = state.Fixture.Database.CreateNativeOperation(OperationKind.Batch, id, MetadataBootstrapState.Actor,
            state.Fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(state.Request));
        state.Receipt = new(id, new(state.Fixture.Store.Identity.Incarnation, state.Partition.AtomicPartitionId, state.Fixture.Store.Position + 1L, 1L),
            [new("putDocument", MetadataBootstrapState.Collection, MetadataBootstrapState.Entity, 1L),
             new("appendEvents", MetadataBootstrapState.Streams, MetadataBootstrapState.Entity, 1L),
             new("enqueue", MetadataBootstrapState.Queue, MetadataBootstrapState.Entity, 1L)], DurabilityProfile.ProcessDurable);
        token.ThrowIfCancellationRequested();
        var actual = state.Fixture.Database.Apply(state.Command).Get<CommitReceipt>();
        state.CommandOutcome = MetadataBootstrapValues.Outcome(state, state.Command);
        state.Image = MetadataBootstrapValues.Image(state);
        state.Position = state.Fixture.Store.Position;
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(state.Fixture.Store, state.Identity, state.Position);
        await ReceiptAsync(state.Receipt, actual);
        await RequireAsync(state, token);
    }
    internal static async Task RequireAsync(MetadataBootstrapState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapProducers.CatalogAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapPolicy.FenceAsync(state, true), failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await Assert.That(state.Fixture.Database.Authenticate(MetadataBootstrapState.Secret,
                state.Fixture.Database.EvaluationClock.GetUtcNow())).IsEqualTo(MetadataBootstrapState.Actor);
            await MetadataBootstrapValues.ResultAsync(new(null)
            {
                NativeValue = true
            }, state.Fixture.Database.Apply(state.Fresh));
            await Assert.That(MetadataBootstrapValues.Outcome(state, state.Fresh).AsSpan().SequenceEqual(state.FreshOutcome)).IsTrue();
        }, failures);
        await ServerFailureObserver.ObserveAsync(async () => await ReceiptAsync(state.Receipt,
            state.Fixture.Database.Apply(state.Command).Get<CommitReceipt>()), failures);
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(MetadataBootstrapValues.Outcome(state, state.Command)
            .AsSpan().SequenceEqual(state.CommandOutcome)).IsTrue(), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapModels.RequireAsync(state), failures);
        await ServerFailureObserver.ObserveAsync(() => MetadataBootstrapValues.ConservedAsync(state, state.Image, state.Identity, state.Position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await MetadataBootstrapValues.EqualAsync(expected, actual);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
}
