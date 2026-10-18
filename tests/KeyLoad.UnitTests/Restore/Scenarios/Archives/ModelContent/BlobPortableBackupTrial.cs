using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BlobPortableBackupTrial
{
    private const string MissingBeginClock = "The genuine blob Begin committed clock is missing.";
    internal static async Task RunAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new BackupIndexedDocumentDedupFixture();
            await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(BackupIndexedDocumentDedupFixture fixture, CancellationToken token)
    {
        var source = fixture.Source.Database;
        var definition = new ResourceDefinition(BlobPortableBackupSeed.Resource, ResourceKind.BlobStore,
            fixture.Partition.TransactionDomainId);
        BlobPortableBackupSeed.Send(source, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, definition), token);
        var bytes = BlobPortableBackupSeed.Payload();
        var published = await BlobPortableBackupSeed.PublishAsync(source,
            new(fixture.Partition, BlobPortableBackupSeed.Resource, BlobPortableBackupSeed.Published), bytes, token);
        var active = new BlobRef(fixture.Partition, BlobPortableBackupSeed.Resource, BlobPortableBackupSeed.Active);
        var upload = Guid.NewGuid();
        var id = Guid.NewGuid();
        var begin = BlobPortableBackupSeed.Send(source, OperationKind.BeginBlobUpload, id,
            new BeginBlobUploadRequest(id, active, upload, bytes.Length, 0), token);
        var beginClock = source.Store.Read(view => NativeSerialization.Deserialize<DateTimeOffset>(
            view.ReadOwnedValue(KeySpace.ClockBytes) ?? throw new InvalidOperationException(MissingBeginClock)));
        await Assert.That(beginClock).IsGreaterThanOrEqualTo(new DateTimeOffset(begin.Operation.Timestamp));
        await BlobPortableBackupAssertions.ReceiptAsync(source, active,
            begin.Result.Get<BlobCommitResult<BlobUploadInfo>>().Receipt, id);
        var accepted = bytes.AsMemory(0, BlobLimits.RawPartBytes).ToArray();
        var part = await BlobPortableBackupSeed.PartAsync(source, active, upload, 0, accepted, token);
        var originalInfo = part.Result.Get<BlobCommitResult<BlobUploadInfo>>().Value;
        var activeHash = BlobIntegrity.InitialHash(source.Store.Identity.Incarnation, active, upload, bytes.Length);
        activeHash = BlobIntegrity.NextHash(activeHash, 0, accepted.Length, BlobIntegrity.PartHash(accepted));
        await Assert.That(originalInfo.ExpiresAt).IsEqualTo(beginClock.AddSeconds(new BlobPolicy().UploadTtlSeconds));
        await RequireUploadInfoAsync(new BlobUploadInfo(active, upload, bytes.Length,
            0L, 1, accepted.Length, beginClock.AddSeconds(new BlobPolicy().UploadTtlSeconds),
            BlobUploadStatus.Active, activeHash), originalInfo);
        var creator = source.Store.Read(view => BlobRecordReader.Get<BlobState>(view, BlobKeys.State(active, upload)));
        await Assert.That(creator!.CreatorPrincipalId).IsEqualTo(BlobPortableBackupSeed.Root);
        await BlobPortableBackupAssertions.RangesAsync(source, published.Metadata, bytes, token);
        await BlobPortableBackupAssertions.ReplayAsync(source, [.. published.Originals, begin, part], token);
        var originals = new[] { begin, part }.Concat(published.Originals).ToArray();
        var outcomes = originals.Select(original => BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(
            source.Store, fixture.Partition, BlobPortableBackupSeed.Root, original.Operation.CommandId)).ToArray();
        var sourceCut = BackupPersistedPolicyAssertions.Capture(source.Store);
        await Assert.That(fixture.CreateBackup()).IsEqualTo(sourceCut.Position);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.BackupDirectory, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.BackupDirectory, UnitExecutionOptions.StorageExecution());
        await Assert.That(verified.Position).IsEqualTo(sourceCut.Position);
        await BackupPersistedPolicyAssertions.SameAsync(source.Store.Identity, verified.Identity);
        var restored = await RestoreAsync(fixture, token);
        await OutcomesAsync(fixture, originals, outcomes);
        await BlobPortableBackupAssertions.FencedAsync(restored, published.Metadata, part.Operation, token);
        new BlobStorageOperations(restored).NormalizeRestoredStore();
        await BlobPortableBackupAssertions.RangesAsync(restored, published.Metadata, bytes, token);
        await BlobPortableBackupAssertions.ActiveAsync(restored, originalInfo, accepted, token);
        var fresh = await BlobPortableBackupSeed.PublishAsync(restored,
            new(fixture.Partition, BlobPortableBackupSeed.Resource, BlobPortableBackupSeed.Fresh), bytes, token);
        await ColdAsync(fixture, fresh, published.Metadata, originalInfo, bytes, accepted, token);
        await OutcomesAsync(fixture, originals, outcomes);
        await BackupPersistedPolicyAssertions.UnchangedAsync(source.Store, sourceCut);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.BackupDirectory, token);
    }

    private static async Task RequireUploadInfoAsync(BlobUploadInfo expected, BlobUploadInfo actual)
    {
        var actualBytes = NativeSerialization.Serialize(actual);
        var expectedBytes = NativeSerialization.Serialize(expected);
        await Assert.That(actualBytes.AsSpan().SequenceEqual(expectedBytes)).IsTrue()
            .Because(DescribeUploadInfo(expected, actual, expectedBytes, actualBytes));
    }

    private static string DescribeUploadInfo(BlobUploadInfo expected, BlobUploadInfo actual,
        byte[] expectedBytes, byte[] actualBytes)
    {
        string[] left = [expected.Blob.Partition.TenantId, expected.Blob.Partition.DatabaseId,
            expected.Blob.Partition.TransactionDomainId, expected.Blob.Partition.PartitionKey,
            expected.Blob.Resource, expected.Blob.Id, expected.IntegrityHash];
        string[] right = [actual.Blob.Partition.TenantId, actual.Blob.Partition.DatabaseId,
            actual.Blob.Partition.TransactionDomainId, actual.Blob.Partition.PartitionKey,
            actual.Blob.Resource, actual.Blob.Id, actual.IntegrityHash];
        var aliases = true;
        for (var index = 0; index < left.Length; index++)
        {
            for (var other = 0; other < left.Length; other++)
            {
                aliases &= ReferenceEquals(left[index], left[other]) == ReferenceEquals(right[index], right[other]);
            }
        }
        var types = expected.GetType() == actual.GetType()
            && expected.Blob.GetType() == actual.Blob.GetType()
            && expected.Blob.Partition.GetType() == actual.Blob.Partition.GetType();
        var offset = 0;
        while (offset < Math.Min(expectedBytes.Length, actualBytes.Length)
            && expectedBytes[offset] == actualBytes[offset])
        {
            offset++;
        }
        return $"BlobUploadInfo blob={expected.Blob == actual.Blob} upload={expected.UploadId == actual.UploadId} "
            + $"declaredLength={expected.DeclaredLength == actual.DeclaredLength} revision={expected.ExpectedRevision == actual.ExpectedRevision} "
            + $"ordinal={expected.NextOrdinal == actual.NextOrdinal} storedBytes={expected.StoredBytes == actual.StoredBytes} "
            + $"expiryExact={expected.ExpiresAt.EqualsExact(actual.ExpiresAt)} status={expected.Status == actual.Status} hash={expected.IntegrityHash == actual.IntegrityHash}; "
            + $"selectedStringSlots={left.Length} aliasTopology={aliases} runtimeTypes={types}; "
            + $"bytes expected={expectedBytes.Length} actual={actualBytes.Length} firstDifference={offset}.";
    }

    private static async Task OutcomesAsync(BackupIndexedDocumentDedupFixture fixture,
        (Command<DatabaseCommand> Operation, Result<object> Result)[] originals, byte[][] outcomes)
    {
        for (var index = 0; index < originals.Length; index++)
        {
            var actual = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.TargetStore,
                fixture.Partition, BlobPortableBackupSeed.Root, originals[index].Operation.CommandId);
            await Assert.That(actual.AsSpan().SequenceEqual(outcomes[index])).IsTrue();
        }
    }

    private static async Task<DatabaseEngine> RestoreAsync(BackupIndexedDocumentDedupFixture fixture,
        CancellationToken token)
    {
        var result = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.BackupDirectory, fixture.TargetDirectory], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var database = fixture.OpenRestored();
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, fixture.TargetStore.Identity);
        await Assert.That(fixture.TargetStore.Identity.NodeId).IsNotEqualTo(fixture.Source.Store.Identity.NodeId);
        await Assert.That(fixture.TargetStore.Identity.Incarnation).IsNotEqualTo(fixture.Source.Store.Identity.Incarnation);
        await Assert.That(fixture.TargetStore.Identity.DispatchPaused).IsTrue();
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(fixture.Source.Store.Position + 1L);
        return database;
    }

    private static async Task ColdAsync(BackupIndexedDocumentDedupFixture fixture,
        (BlobMetadata Metadata, (Command<DatabaseCommand> Operation, Result<object> Result)[] Originals) fresh,
        BlobMetadata published, BlobUploadInfo active, byte[] payload, byte[] accepted, CancellationToken token)
    {
        var database = fixture.RestoredDatabase;
        await BlobPortableBackupAssertions.RangesAsync(database, fresh.Metadata, payload, token);
        await BlobPortableBackupAssertions.ReplayAsync(database, fresh.Originals, token);
        var identity = database.Store.Identity;
        var cut = BackupPersistedPolicyAssertions.Capture(database.Store);
        for (var cold = 0; cold < 2; cold++)
        {
            fixture.CloseRestored();
            database = fixture.OpenRestored();
            await BackupPersistedPolicyAssertions.SameAsync(identity, database.Store.Identity);
            await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, cut);
            await BlobPortableBackupAssertions.RangesAsync(database, published, payload, token);
            await BlobPortableBackupAssertions.ActiveAsync(database, active, accepted, token);
            await BlobPortableBackupAssertions.RangesAsync(database, fresh.Metadata, payload, token);
            await BlobPortableBackupAssertions.ReplayAsync(database, fresh.Originals, token);
            await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, cut);
        }
    }
}
