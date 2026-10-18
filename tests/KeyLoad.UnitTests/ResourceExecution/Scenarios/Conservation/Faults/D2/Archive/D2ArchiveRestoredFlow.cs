using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveRestoredFlow
{
    internal static async Task ExecuteAsync(D2ArchiveSlot slot, CancellationToken token)
    {
        var healthy = await WarmAsync(slot, token);
        for (var pass = D2ArchiveData.Empty; pass < D2ArchiveData.TwoCold; pass++)
        {
            var failures = new List<Exception>();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = Open(slot);
                var database = Create(store, slot.Mapping.Target);
                await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                {
                    await IdentityAsync(store, slot);
                    await ReplayAsync(database, store, slot, healthy, token);
                }, failures);
            }, failures);
            KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task<D2ArchiveHealthy> WarmAsync(D2ArchiveSlot slot, CancellationToken token)
    {
        D2ArchiveHealthy? healthy = null;
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var store = Open(slot);
            var database = Create(store, slot.Mapping.Target);
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await IdentityAsync(store, slot);
                await Assert.That(database.LastApplied).IsEqualTo(D2ArchiveData.Empty);
                await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))).IsNull();
                await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.ClockBytes))).IsNull();
                await D2ArchiveAssertions.DataAsync(database, slot.Archive.Source, false);
                var refusal = await D2ArchiveAssertions.GenerationAsync(database, store, slot.Archive.Source);
                await D2ArchiveQuery.VerifyAsync(database, store, slot.Archive.Source,
                    new(slot.Mapping.Target, slot.Mapping.Endpoints), token);
                var written = await WriteAsync(database, store, slot.Archive.Source, token);
                var retained = store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(
                    slot.Archive.Source.Partition, D2ArchiveData.Root, written.Original.Id)))
                    ?? throw new InvalidOperationException("The restored native outcome is missing.");
                healthy = new(written.Original, written.Outcome, retained, refusal, store.Position,
                    ControlledPartitionMovementRawImage.Bytes(store));
                await ReplayAsync(database, store, slot, healthy, token);
            }, failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return healthy ?? throw new InvalidOperationException("The genuine restored-incarnation command did not complete.");
    }

    private static async Task<(ReplicatedOperation Original, OperationResult Outcome)> WriteAsync(
        DatabaseEngine database, ZoneTreeStore store, D2ArchiveSource source, CancellationToken token)
    {
        var position = store.Position;
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, source.Partition,
            [new PutDocument(source.DocumentsLifetime.ResourceName, D2ArchiveData.HealthyId,
                D2ArchiveData.HealthyJson, D2ArchiveData.Empty)]);
        var original = database.CreateNativeOperation(OperationKind.Batch, id, D2ArchiveData.Root,
            database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
        var result = database.ApplyEmbedded(original, token);
        await Assert.That(result.Error).IsNull();
        var expected = new CommitReceipt(id, new(store.Identity.Incarnation, source.Partition.AtomicPartitionId,
            checked(position + D2ArchiveData.Single), D2ArchiveData.Single),
            [new(D2ArchiveData.PutDocumentKind, source.DocumentsLifetime.ResourceName, D2ArchiveData.HealthyId,
                D2ArchiveData.Single)], DurabilityProfile.ProcessDurable);
        await D2ResourceInventoryAssertions.EqualAsync(expected, result.Get<CommitReceipt>());
        return (original, result);
    }

    private static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, D2ArchiveSlot slot,
        D2ArchiveHealthy healthy, CancellationToken token)
    {
        await Assert.That(database.LastApplied).IsEqualTo(D2ArchiveData.Empty);
        await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))).IsNull();
        await Assert.That(store.Position).IsEqualTo(healthy.Position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(store)
            .SequenceEqual(healthy.Image, StringComparer.Ordinal)).IsTrue();
        var old = await D2ArchiveAssertions.GenerationAsync(database, store, slot.Archive.Source);
        await D2ResourceInventoryAssertions.EqualAsync(healthy.OldGenerationRefusal, old);
        var replay = database.ApplyEmbedded(healthy.Original, token);
        await D2ResourceInventoryAssertions.EqualAsync(healthy.Outcome, replay);
        await D2ResourceInventoryAssertions.EqualAsync(healthy.Outcome.Get<CommitReceipt>(), replay.Get<CommitReceipt>());
        await D2ArchiveAssertions.RetainedAsync(store, slot.Archive.Source.Partition, healthy.Original.Id, healthy.RetainedOutcome);
        await D2ArchiveAssertions.DataAsync(database, slot.Archive.Source, true);
        await D2ArchiveQuery.VerifyAsync(database, store, slot.Archive.Source,
            new(slot.Mapping.Target, slot.Mapping.Endpoints), token);
        await Assert.That(store.Position).IsEqualTo(healthy.Position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(store)
            .SequenceEqual(healthy.Image, StringComparer.Ordinal)).IsTrue();
    }

    private static async Task IdentityAsync(ZoneTreeStore store, D2ArchiveSlot slot)
    {
        await Assert.That(store.Identity.NodeId).IsEqualTo(slot.Context.TargetNodeId);
        await Assert.That(store.Identity.Incarnation).IsEqualTo(slot.Mapping.Target.Incarnation);
        await Assert.That(store.Identity.SigningKey.Span.SequenceEqual(slot.SigningKey.Span)).IsTrue();
        await Assert.That(store.Identity.DispatchPaused).IsTrue();
    }

    private static ZoneTreeStore Open(D2ArchiveSlot slot)
        => new(new ZoneTreeStoreOptions(slot.Destination) { Incarnation = slot.Mapping.Target.Incarnation },
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static DatabaseEngine Create(ZoneTreeStore store, PhysicalShardRecord owner)
        => TestDatabaseEngineFactory.Capture(UnitExecutionOptions.DatabaseLimits(), UnitExecutionOptions.BlobExecution(),
            UnitExecutionOptions.NativeClaimsExecution(), null, TimeProvider.System)(store, owner);
}
