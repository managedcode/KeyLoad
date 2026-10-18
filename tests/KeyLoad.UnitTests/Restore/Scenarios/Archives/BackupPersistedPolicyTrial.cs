using KeyLoad.Core;
using KeyLoad.Server;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupPersistedPolicyTrial
{
    private const string MissingAppliedPosition = "The canonical applied position is invalid.";
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
        var original = await BackupPersistedPolicySeed.RunAsync(fixture, token);
        var source = BackupPersistedPolicyAssertions.Capture(fixture.Source.Store);
        var sourceIdentity = fixture.Source.Store.Identity;
        var sourceOutcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Partition, BackupPersistedPolicySeed.Root, original.Operation.CommandId);
        var backupCut = fixture.CreateBackup();
        await Assert.That(backupCut).IsEqualTo(source.Position);
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.BackupDirectory, token);
        var verified = KeyLoad.Storage.ZoneTree.ZoneTreeStore.VerifyBackup(fixture.BackupDirectory, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(source.Position);
        await BackupPersistedPolicyAssertions.SameAsync(sourceIdentity, verified.Identity);
        var restore = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
            ["restore", fixture.BackupDirectory, fixture.TargetDirectory], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore);
        var database = fixture.OpenRestored();
        await Assert.That(fixture.TargetStore.Identity.NodeId).IsNotEqualTo(sourceIdentity.NodeId);
        await Assert.That(fixture.TargetStore.Identity.Incarnation).IsNotEqualTo(sourceIdentity.Incarnation);
        await Assert.That(fixture.TargetStore.Identity.DispatchPaused).IsTrue();
        await Assert.That(fixture.TargetStore.Position).IsEqualTo(source.Position + 1L);
        await Assert.That(BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.TargetStore, fixture.Partition,
            BackupPersistedPolicySeed.Root, original.Operation.CommandId).AsSpan().SequenceEqual(sourceOutcome)).IsTrue();
        await RefuseOriginalAsync(fixture, database, original.Operation, original.Result, token);
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, false, token);
        var restoredIdentity = fixture.TargetStore.Identity;
        var denied = BackupPersistedPolicyAssertions.Capture(fixture.TargetStore);
        fixture.CloseRestored();
        database = fixture.OpenRestored();
        await BackupPersistedPolicyAssertions.SameAsync(restoredIdentity, fixture.TargetStore.Identity);
        await BackupPersistedPolicyAssertions.UnchangedAsync(fixture.TargetStore, denied);
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, false, token);
        await RepairAndColdAsync(fixture, database, token);
        await BackupPersistedPolicyAssertions.UnchangedAsync(fixture.Source.Store, source);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.BackupDirectory, token);
    }

    private static async Task RepairAndColdAsync(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database, CancellationToken token)
    {
        var restoredIdentity = fixture.TargetStore.Identity;
        var repair = BackupPersistedPolicySeed.Send(database, OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(BackupPersistedPolicySeed.Principal(fixture.Partition, true)), token);
        await BackupPersistedPolicyAssertions.SameAsync(BackupPersistedPolicySeed.Principal(fixture.Partition, true), repair.Get<PrincipalRecord>());
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, true, token);
        var id = Guid.NewGuid();
        var healthy = BackupPersistedPolicySeed.Create(database, OperationKind.Batch,
            new CommandRequest(id, fixture.Partition, [new PutDocument(BackupPersistedPolicySeed.Collection,
                BackupPersistedPolicySeed.HealthyId, BackupPersistedPolicySeed.HealthyJson)]), id);
        var result = database.ApplyEmbedded(healthy, token);
        await Assert.That(Errors.Code(result.Problem)).IsNull();
        await BackupPersistedPolicyAssertions.ReceiptAsync(database, fixture.Partition, id, result.Get<CommitReceipt>());
        await HealthyDocumentAsync(fixture, database, token);
        await BackupPersistedPolicyAssertions.ReplayAsync(database, healthy, result, token);
        var final = BackupPersistedPolicyAssertions.Capture(fixture.TargetStore);
        fixture.CloseRestored();
        database = fixture.OpenRestored();
        await BackupPersistedPolicyAssertions.SameAsync(restoredIdentity, fixture.TargetStore.Identity);
        await BackupPersistedPolicyAssertions.UnchangedAsync(fixture.TargetStore, final);
        await BackupPersistedPolicyAssertions.CurrentAsync(fixture, database, true, token);
        await HealthyDocumentAsync(fixture, database, token);
        await BackupPersistedPolicyAssertions.ReplayAsync(database, healthy, result, token);
    }

    private static async Task HealthyDocumentAsync(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database, CancellationToken token)
    {
        var cut = BackupPersistedPolicyAssertions.Capture(database.Store);
        var reference = new EntityRef(fixture.Partition, BackupPersistedPolicySeed.Collection, BackupPersistedPolicySeed.HealthyId);
        await BackupPersistedPolicyAssertions.SameAsync(new DocumentResult(reference, 1L,
            BackupPersistedPolicySeed.HealthyJson, false, []), database.GetDocument(BackupPersistedPolicySeed.Reader,
                reference, cancellationToken: token));
        await BackupPersistedPolicyAssertions.UnchangedAsync(database.Store, cut);
    }

    private static async Task RefuseOriginalAsync(BackupIndexedDocumentDedupFixture fixture,
        DatabaseEngine database, Command<DatabaseCommand> operation, Result<object> result, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var cut = BackupPersistedPolicyAssertions.Capture(fixture.TargetStore);
        var authority = Assert.ThrowsExactly<KeyLoadException>(() => database.Apply(operation));
        await Assert.That(authority.Code).IsEqualTo(ErrorCode.Corruption);
        var originalToken = result.Get<CommitReceipt>().Token;
        var owner = database.Store.Read(view => DatabaseEngine.ReadPlacementWitness(view, fixture.Partition));
        await Assert.That(owner.Incarnation).IsEqualTo(originalToken.Incarnation);

        await Assert.That(originalToken.AtomicPartitionId).IsEqualTo(fixture.Partition.AtomicPartitionId);
        await Assert.That(originalToken.Position).IsGreaterThan(0L);
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(KeySpace.AppliedBytes))).IsNull();
        var reference = new EntityRef(fixture.Partition, BackupPersistedPolicySeed.Collection, BackupPersistedPolicySeed.DocumentId);
        var error = Assert.ThrowsExactly<KeyLoadException>(() => database.GetDocument(BackupPersistedPolicySeed.Reader,
            reference, originalToken, token));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(MissingAppliedPosition);
        await BackupPersistedPolicyAssertions.UnchangedAsync(fixture.TargetStore, cut);
    }
}
