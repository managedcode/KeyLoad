using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.BackupRestore.InitialConservationLineageProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class InitialConservationArchiveFlow
{
    internal static async Task<InitialConservationArchive> CaptureAsync(ZoneTreeStore store,
        DatabaseEngine database, string directory, CancellationToken token)
    {
        var rows = EventingArtifactState.FullBytes(store);
        var identity = store.Identity;
        var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), TimeProvider.System, token);
        var captureId = Guid.NewGuid();
        var capture = store.CreateCatalogBackup(directory, (view, position, actualIdentity) =>
            NativeSerialization.Serialize(database.CaptureClusterBackupOwner(view, P.Root,
                captureId, position, actualIdentity, work)), token);
        var cut = NativeSerialization.Deserialize<ClusterBackupOwnerCut>(capture.Metadata.Span);
        await Assert.That(cut.CaptureId).IsEqualTo(captureId);
        await Assert.That(cut.SourceNodeId).IsEqualTo(identity.NodeId);
        await Assert.That(cut.StorePosition).IsEqualTo(store.Position);
        await Assert.That(cut.AppliedIndex).IsEqualTo(database.LastApplied);
        var result = new InitialConservationArchive(directory, capture, cut, identity, rows,
            await NativeBackupJournalRepairAssertions.ArchiveAsync(directory, token));
        await UnchangedAsync(result, store, token);
        return result;
    }

    internal static async Task UnchangedAsync(InitialConservationArchive archive, ZoneTreeStore source,
        CancellationToken token)
    {
        await InitialConservationLineageAssertions.EqualAsync(archive.Identity, source.Identity);
        await Assert.That(source.Position).IsEqualTo(archive.Capture.Position);
        await Assert.That(EventingArtifactState.FullBytes(source).SequenceEqual(archive.SourceRows, StringComparer.Ordinal)).IsTrue();
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive.Files, archive.Directory, token);
    }

    internal static async Task<InitialConservationArchive> CaptureNextAsync(EventingArtifactFixture fixture,
        InitialConservationSlot first, CancellationToken token)
    {
        ZoneTreeStore? store = null;
        TestDatabaseReplicaAdmission? admission = null;
        InitialConservationArchive? result = null;
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            store = new(new(first.Destination), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
            var database = fixture.Source.CreateConfiguredDatabase(store);
            admission = fixture.Source.CreateTargetAdmission(database, store, first.Destination,
                first.Destination + TestDatabaseReplicaDirectory.Suffix, originalNodeId: null);
            var id = Guid.NewGuid();
            var before = admission.ReadJournalCut().LastIndex;
            var command = new CommandRequest(id, fixture.Source.Partition,
                [new PutDocument(P.Collection, P.Document, P.UpdatedJson, P.One)]);
            var original = database.CreateNativeOperation(OperationKind.Batch, id, P.Root,
                database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(command));
            var actual = admission.Submit(original, explicitTime: true, token).Get<CommitReceipt>();
            var expected = new CommitReceipt(id, new(store.Identity.Incarnation,
                fixture.Source.Partition.AtomicPartitionId, checked(before + P.One), first.Mapping.Target.PlacementEpoch),
                [new(P.PutKind, P.Collection, P.Document, P.Two)], DurabilityProfile.ProcessDurable);
            await InitialConservationLineageAssertions.EqualAsync(expected, actual);
            await InitialConservationLineageAssertions.EqualAsync(actual,
                admission.Submit(original, explicitTime: true, token).Get<CommitReceipt>());
            await InitialConservationLineageAssertions.DocumentAsync(database, fixture.Source.Partition, P.Two, P.UpdatedJson, token);
            result = await CaptureAsync(store, database, Path.Combine(fixture.Root, P.SecondArchive), token);
        }, failures);
        if (admission is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(admission.Dispose, failures);
        }

        if (store is not null)
        {
            KeyLoad.Server.ServerFailureObserver.Observe(store.Dispose, failures);
        }

        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException("The actual next canonical archive did not complete.");
    }
}
