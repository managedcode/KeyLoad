using System.ComponentModel;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class BackupRequiredFileAccessFlow
{
    private const string JournalName = "commands.wal";
    private const string DestinationName = "required-file-access-repaired";
    private const string RestoreCommand = "restore";
    private const int PermissionDeniedErrno = 13;
    private const string FileAccessFailure = "Offline filesystem operation failed.";

    internal static async Task RunAsync(bool existingEmpty, CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new MetadataBackupFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(options, fixture, existingEmpty, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.Features.TestInfrastructure.TestExecutionOptions> options,
        MetadataBackupFixture fixture, bool existingEmpty, CancellationToken token)
    {
        var source = BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token);
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.BackupDirectory, token);
        var destination = Path.Combine(fixture.BackupDirectory, DestinationName);
        if (existingEmpty)
        {
            Directory.CreateDirectory(destination);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var region = new BackupArchivePermissionRegion(Path.Combine(fixture.BackupDirectory, JournalName));
            await ServerFailureObserver.ObserveAsync(() => RefuseAsync(options, fixture, destination,
                existingEmpty, region, original, source, token), failures);
            ServerFailureObserver.Observe(region.Repair, failures);
            await ServerFailureObserver.ObserveAsync(() => BackupJournalRepairAssertions.SameArchiveAsync(
                original, fixture.BackupDirectory, CancellationToken.None), failures);
            await ServerFailureObserver.ObserveAsync(() => BackupJournalRepairAssertions.SameCutAsync(source,
                BackupJournalRepairAssertions.Read(fixture.SourceDirectory, CancellationToken.None)), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.BackupDirectory, destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        await MetadataOuterChecksumRepairAssertions.RestoredAsync(fixture, destination, result, source.Position, token);
        await MetadataOuterChecksumRepairCold.RunAsync(destination, token);
        await MetadataOuterChecksumRepairAssertions.OriginalArchiveAsync(original, fixture.BackupDirectory, destination, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
    }

    private static async Task RefuseAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.Features.TestInfrastructure.TestExecutionOptions> options,
        MetadataBackupFixture fixture, string destination, bool existingEmpty,
        BackupArchivePermissionRegion region, Dictionary<string, byte[]> original,
        (StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        region.DenyNewReaders();
        var execution = Assert.ThrowsExactly<IOException>(() => ZoneTreeStore.Restore(
            fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));
        await Assert.That(execution).IsNotNull();
        await Assert.That(execution!.Message).IsEqualTo(FileAccessFailure);
        await Assert.That(execution.InnerException is Win32Exception { NativeErrorCode: PermissionDeniedErrno }).IsTrue();
        await BackupRequiredFileAccessAssertions.NoPublicationAsync(fixture.BackupDirectory, destination, existingEmpty);
        await region.RequireOriginalAsync(original[JournalName], token);
        await BackupRequiredFileAccessAssertions.ImmutableInputsAsync(fixture, original, source, token);
        var cli = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.BackupDirectory, destination], token);
        await BackupRequiredFileAccessAssertions.RefusedCliAsync(cli);
        await BackupRequiredFileAccessAssertions.NoPublicationAsync(fixture.BackupDirectory, destination, existingEmpty);
        await region.RequireOriginalAsync(original[JournalName], token);
        await BackupRequiredFileAccessAssertions.ImmutableInputsAsync(fixture, original, source, token);
    }
}
