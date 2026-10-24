using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.BackupRestore;

internal static class MetadataOuterChecksumRepairFlow
{
    private const string RestoreCommand = "restore";
    private const string RestoreStagingPattern = ".keyload-restore-*";

    internal static async Task RunAsync(CancellationToken token)
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new MetadataBackupFixture();
            await ServerFailureObserver.ObserveAsync(() => CompleteAsync(options, fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        MetadataBackupFixture fixture, CancellationToken token)
    {
        var source = BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token);
        var original = await BackupJournalRepairAssertions.ArchiveAsync(fixture.BackupDirectory, token);
        var destination = Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityChecksumRestorePath);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(
            () => RefuseAsync(options, fixture, destination, original, source, token), failures);
        await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(
            Path.Combine(fixture.BackupDirectory, MetadataTestContract.IdentityFileName),
            original[MetadataTestContract.IdentityFileName], CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() =>
            BackupJournalRepairAssertions.SameArchiveAsync(original, fixture.BackupDirectory,
                CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, CancellationToken.None)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        var result = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, fixture.BackupDirectory, destination], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(result);
        await MetadataOuterChecksumRepairAssertions.RestoredAsync(fixture, destination, result, source.Position, token);
        await MetadataOuterChecksumRepairCold.RunAsync(destination, token);
        await MetadataOuterChecksumRepairAssertions.OriginalArchiveAsync(original, fixture.BackupDirectory,
            destination, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
    }

    private static async Task RefuseAsync(
        global::Microsoft.Extensions.Options.IOptions<global::KeyLoad.AppHost.TestInfrastructure.TestExecutionOptions> options,
        MetadataBackupFixture fixture, string destination,
        Dictionary<string, byte[]> original,
        (StoreIdentity Identity, long Position, string[] Rows) source, CancellationToken token)
    {
        var tampered = original.ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        var identity = original[MetadataTestContract.IdentityFileName].ToArray();
        identity[0] ^= 1;
        tampered[MetadataTestContract.IdentityFileName] = identity;
        await File.WriteAllBytesAsync(Path.Combine(fixture.BackupDirectory,
            MetadataTestContract.IdentityFileName), identity, token);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ZoneTreeStore.Restore(
            fixture.BackupDirectory, destination, UnitExecutionOptions.StorageExecution()));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo(MetadataTestContract.BackupFileVerificationFailedDetail);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        await MetadataOuterChecksumRepairAssertions.RefusedCliAsync(
            await CliBackupRestoreProcess.RunAsync(options,
                [RestoreCommand, fixture.BackupDirectory, destination], token), destination);
        await BackupJournalRepairAssertions.SameArchiveAsync(tampered, fixture.BackupDirectory, token);
        await BackupJournalRepairAssertions.SameCutAsync(source,
            BackupJournalRepairAssertions.Read(fixture.SourceDirectory, token));
        var root = Directory.GetParent(fixture.BackupDirectory)
            ?? throw new DirectoryNotFoundException();
        await Assert.That(Directory.EnumerateDirectories(root.FullName, RestoreStagingPattern,
            SearchOption.AllDirectories).Any()).IsFalse();
    }
}
