using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.BackupRestore.FreshArchiveConservationProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class FreshArchiveConservationFlow
{
    internal static async Task RunAsync(bool malformedFloor, bool existingEmpty, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), P.Prefix + Guid.NewGuid().ToString("N"));
        var failures = new List<Exception>();
        TestDatabase? source = null;
        var ownedRoot = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (Directory.Exists(root))
            {
                throw new InvalidOperationException(P.Invalid);
            }

            Directory.CreateDirectory(root);
            ownedRoot = true;
            source = new(directory: Path.Combine(root, P.Source), nativeReplicaAdmission: true,
                nativeReplicaDirectory: Path.Combine(root, P.Source + TestDatabaseReplicaDirectory.Suffix));
            await ExerciseAsync(source, root, malformedFloor, existingEmpty, token);
        }, failures);
        if (source is not null)
        {
            ServerFailureObserver.Observe(source.JoinOwnedResources, failures);
        }

        if (failures.Count == 0 && ownedRoot)
        {
            if (source is not null)
            {
                ServerFailureObserver.Observe(source.Dispose, failures);
            }

            if (failures.Count == 0)
            {
                ServerFailureObserver.Observe(() => Directory.Delete(root, true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(TestDatabase source, string root, bool malformedFloor,
        bool existingEmpty, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        source.Configure(P.Collection, ResourceKind.Collection);
        var commandId = Guid.NewGuid();
        var operation = source.Database.CreateNativeOperation(OperationKind.Batch, commandId, P.Root,
            source.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(new CommandRequest(commandId,
                source.Partition, [new PutDocument(P.Collection, P.Document, P.Json, 0)])));
        var expected = new CommitReceipt(commandId, new(source.Store.Identity.Incarnation,
            source.Partition.AtomicPartitionId, checked(source.ReadJournalCut().LastIndex + 1), 1),
            [new(P.PutKind, P.Collection, P.Document, 1)], DurabilityProfile.ProcessDurable);
        var actual = source.SubmitIssued(operation).Get<CommitReceipt>();
        await FreshArchiveConservationAssertions.EqualAsync(expected, actual);
        await Assert.That(actual.Mutations.Single().CompositionReferences.IsEmpty).IsTrue();
        CorruptSource(source, malformedFloor);
        var cut = FreshArchiveConservationAssertions.Capture(source.Store);
        var archive = Path.Combine(root, P.Archive);
        var target = Path.Combine(root, P.Target);
        await Assert.That(source.Store.CreateBackup(archive)).IsEqualTo(cut.Position);
        var originalArchive = await NativeBackupJournalRepairAssertions.ArchiveAsync(archive, token);
        if (existingEmpty)
        {
            Directory.CreateDirectory(target);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(archive, target,
                UnitExecutionOptions.StorageExecution()));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await FreshArchiveConservationAssertions.UnpublishedAsync(root, target, existingEmpty);
            await NativeBackupJournalRepairAssertions.SameCutAsync(cut, FreshArchiveConservationAssertions.Capture(source.Store));
            await NativeBackupJournalRepairAssertions.SameArchiveAsync(originalArchive, archive, token);
        }
        source.JoinOwnedResources();
        for (var cold = 0; cold < 2; cold++)
        {
            // Bare native storage treats canonical values as opaque; image-domain validation belongs to restore admission.
            await NativeBackupJournalRepairAssertions.SameCutAsync(cut,
                NativeBackupJournalRepairAssertions.Read(source.Directory, token));
            await NativeBackupJournalRepairAssertions.SameArchiveAsync(originalArchive, archive, token);
            await FreshArchiveConservationAssertions.UnpublishedAsync(root, target, existingEmpty);
        }
    }

    private static void CorruptSource(TestDatabase source, bool malformedFloor)
    {
        var owner = source.Store.Read(view => PhysicalShardCatalogRecordSerialization.Read(view)!.DefaultShard);
        var lifetime = source.Store.Read(view => ResourceLifetimeCatalog.Read(view, source.Partition.TenantId,
            source.Partition.DatabaseId, P.Collection, source.Database.Resource(view, source.Partition,
                P.Collection, ResourceKind.Collection)))!;
        source.Store.Commit((transaction, nextPosition) =>
        {
            // Intentional corruption fixture ONLY: no release23 operation, receipt, acknowledgement or valid chain is manufactured.
            var floor = KeyCodec.Encode(P.FloorFamily);
            if (malformedFloor)
            {
                transaction.PutRecord(floor, new ResourceRetirementConservationFloorV1(1, owner, -1, 0, new byte[32]));
            }
            else
            {
                transaction.Delete(floor);
                transaction.PutRecord(KeyCodec.Encode(P.StepFamily, owner.PhysicalShardId, owner.Incarnation, 1L),
                    new ResourceRetirementConservationStepV1(1, owner, 1, lifetime, Guid.NewGuid(), Guid.NewGuid(),
                        nextPosition, new byte[32], new byte[32]));
            }
            return true;
        });
    }
}
