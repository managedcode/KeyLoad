using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class ManifestCutRepairTrial
{
    private const string ErrorCodeKey = "errorCode";
    private const string DetailKey = "detail";
    private const string RestoreCommand = "restore";
    private const string ManifestCutMismatch = "The backup journal does not match its manifest cut.";

    internal static async Task RunAsync(bool declaredCutMismatch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new NativeBackupCutFixture(checkpoint: true);
            await ServerFailureObserver.ObserveAsync(
                () => ExerciseAsync(options, fixture, declaredCutMismatch, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(IOptions<TestExecutionOptions> options,
        NativeBackupCutFixture fixture, bool declaredCutMismatch, CancellationToken token)
    {
        var archive = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
        var source = NativeBackupJournalRepairAssertions.Read(fixture.Source, token);
        await RefuseAndRepairAsync(options, fixture, archive, source, declaredCutMismatch, token);
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
        var verified = ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution(), token);
        await Assert.That(verified.Position).IsEqualTo(fixture.Position);
        await Assert.That(NativeSerialization.Serialize(verified.Identity)
            .SequenceEqual(NativeSerialization.Serialize(fixture.Identity))).IsTrue();
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        var restored = NativeBackupJournalRepairAssertions.Read(fixture.Destination, token);
        await CliBackupJournalRepairAssertions.ReceiptAsync(result, restored.Identity);
        await NativeBackupJournalRepairAssertions.RestoredAsync(fixture, restored.Identity, restored);
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(restored,
            NativeBackupJournalRepairAssertions.Read(fixture.Destination, token));
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(archive, fixture.Backup, token);
    }

    private static async Task RefuseAndRepairAsync(IOptions<TestExecutionOptions> options,
        NativeBackupCutFixture fixture, Dictionary<string, byte[]> archive,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source,
        bool declaredCutMismatch, CancellationToken token)
    {
        var manifest = await MetadataTestFiles.ReadManifestAsync(fixture.Backup);
        var failures = new List<Exception>();
        var repairRequired = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            var position = declaredCutMismatch ? checked(manifest.Position + 1L) : -1L;
            repairRequired = true;
            await MetadataTestFiles.WriteManifestAsync(fixture.Backup, manifest with
            {
                Position = position
            });
            var faulted = await NativeBackupJournalRepairAssertions.ArchiveAsync(fixture.Backup, token);
            await RefusedAsync(options, fixture, faulted, source, declaredCutMismatch, token);
            await RefusedAsync(options, fixture, faulted, source, declaredCutMismatch, token);
        }, failures);
        if (repairRequired)
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
                Path.Combine(fixture.Backup, MetadataTestContract.ManifestFileName),
                archive[MetadataTestContract.ManifestFileName], CancellationToken.None), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RefusedAsync(IOptions<TestExecutionOptions> options,
        NativeBackupCutFixture fixture, Dictionary<string, byte[]> faulted,
        (KeyLoad.Storage.StoreIdentity Identity, long Position, string[] Rows) source,
        bool declaredCutMismatch, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var code = declaredCutMismatch ? ErrorCode.Corruption : ErrorCode.FormatUnsupported;
        var detail = declaredCutMismatch ? ManifestCutMismatch : ZoneTreePersistenceFormat.BackupManifestUnsupported;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeStore.VerifyBackup(fixture.Backup, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(detail);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.Backup, fixture.Destination], token);
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeKey).GetString()).IsEqualTo(code.ToString());
        await Assert.That(problem.RootElement.GetProperty(DetailKey).GetString()).IsEqualTo(detail);
        await Assert.That(Directory.Exists(fixture.Destination) || File.Exists(fixture.Destination)).IsFalse();
        await NativeBackupJournalRepairAssertions.SameArchiveAsync(faulted, fixture.Backup, token);
        await NativeBackupJournalRepairAssertions.SameCutAsync(source,
            NativeBackupJournalRepairAssertions.Read(fixture.Source, token));
    }
}
