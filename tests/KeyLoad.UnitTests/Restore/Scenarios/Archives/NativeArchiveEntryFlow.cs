using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using ManagedCode.Communication.Commands;
using P = KeyLoad.UnitTests.Features.BackupRestore.NativeArchiveEntryProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeArchiveEntryFlow
{
    internal static async Task RunAsync(string file, CancellationToken token)
    {
        var failures = new List<Exception>();
        NativeArchiveEntryFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => BodyAsync(fixture, file, token), failures);
            if (failures.Count != P.NoFailures)
            {
                fixture.RetainRoot();
            }
            fixture.Cleanup(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task BodyAsync(NativeArchiveEntryFixture fixture, string file, CancellationToken token)
    {
        fixture.Source.Configure(P.Collection, ResourceKind.Collection, indexes: [new(P.Index, [P.LabelPath], Unique: true)]);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Partition, [new PutDocument(P.Collection, P.Document, P.Before)]);
        var operation = Operation(fixture.Source.Database, command);
        var before = fixture.Source.Store.Position;
        var placement = fixture.Source.Database.ReadAtomicPartitionPlacement(P.Root, fixture.Source.Partition);
        var receipt = fixture.Source.Database.Apply(operation).Get<CommitReceipt>();
        await NativeArchiveEntryAssertions.ReceiptAsync(receipt, command, placement,
            before + P.PositionStep, P.InitialRevision, fixture.Source.Store.Identity.Durability);
        await NativeArchiveEntryAssertions.ContentAsync(fixture.Source.Database, fixture.Source.Partition, healthy: false, token);
        var sourceIdentity = fixture.Source.Store.Identity;
        var cut = fixture.Source.Store.Position;
        var rows = QueueWholeFlowStorage.Bytes(fixture.Source.Store);
        var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(fixture.Source.Store,
            fixture.Source.Partition, P.Root, command.CommandId);
        var failures = new List<Exception>();
        fixture.CloseSource(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(await fixture.ChildAsync(
            [P.BackupCommand, fixture.SourcePath, fixture.Backup], token));
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.Backup);
        await Assert.That(manifest.Position).IsEqualTo(cut);
        var archive = await NativeArchiveEntryAssertions.CaptureAsync(fixture.Backup, token);
        await NativeArchiveEntryRefusal.RunAsync(fixture, file, archive, token);
        await NativeArchiveEntryAssertions.ArchiveAsync(fixture.Backup, archive, token);
        var source = fixture.Open(fixture.SourcePath, sourceIdentity);
        await NativeArchiveEntryAssertions.ImageAsync(source, rows, cut);
        fixture.CloseOpened(failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(await fixture.ChildAsync(
            [P.RestoreCommand, fixture.Backup, fixture.Target], token));
        await NativeArchiveEntryHealthy.RunAsync(fixture, sourceIdentity, cut, operation, outcome, archive, token);
    }

    internal static Command<DatabaseCommand> Operation(DatabaseEngine database, CommandRequest command)
        => database.CreateCommand(OperationKind.Batch, command.CommandId, P.Root, database.EvaluationClock.GetUtcNow(),
            System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(command)));
}
