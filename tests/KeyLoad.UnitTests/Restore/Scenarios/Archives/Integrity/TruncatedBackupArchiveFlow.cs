using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Storage.ZoneTree.Restore;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class TruncatedBackupArchiveFlow
{
    private const string PieceEnvironment = "KEYLOAD_BACKUP__PIECEBYTES";
    private const string PieceValue = "1024";
    private const string BackupCommand = "backup";
    private const string PackCommand = "pack-backup";
    private const string UnpackCommand = "unpack-backup";
    private const string RestoreCommand = "restore";
    private const string Unpacked = "truncated-unpacked";
    private const string Rejected = "truncated-rejected-restore";
    private const string Journal = "commands.wal";
    private const int Half = 2;

    internal static async Task RunAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, bool emptyDestination, CancellationToken token)
    {
        await TruncatedBackupArchiveCold.SeedAsync(fixture.SourceDirectory);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(await CliBackupRestoreProcess.RunAsync(options,
            [BackupCommand, fixture.SourceDirectory, fixture.BackupDirectory], token));
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { [PieceEnvironment] = PieceValue };
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(await CliBackupRestoreProcess.RunAsync(options,
            [PackCommand, fixture.BackupDirectory, fixture.ArtifactPath], token, environment));
        await Assert.That(BackupArtifact.Inspect(fixture.ArtifactPath).Single(entry => entry.Name == Journal).Pieces)
            .IsGreaterThan(1);
        var source = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, token);
        var backup = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, token);
        var original = await File.ReadAllBytesAsync(fixture.ArtifactPath, token);
        var truncated = original.AsSpan(0, original.Length / Half).ToArray();
        var output = Path.Combine(fixture.Root, Unpacked);
        UnixFileMode? mode = null;
        if (emptyDestination)
        {
            Directory.CreateDirectory(output);
            if (!OperatingSystem.IsWindows())
            {
                mode = File.GetUnixFileMode(output);
            }
        }
        await File.WriteAllBytesAsync(fixture.ArtifactPath, truncated, token);
        await TruncatedBackupArchiveAssertions.RefusalAsync(
            fixture, output, emptyDestination, mode, truncated, token);
        var rejected = Path.Combine(fixture.Root, Rejected);
        await TruncatedBackupArchiveAssertions.RejectedRestoreAsync(await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, output, rejected], token), rejected);
        await TruncatedBackupArchiveAssertions.DestinationAsync(output, emptyDestination, mode);
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, token);
        await File.WriteAllBytesAsync(fixture.ArtifactPath, original, token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(await CliBackupRestoreProcess.RunAsync(options,
            [UnpackCommand, fixture.ArtifactPath, output], token));
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, output, token);
        var restored = await CliBackupRestoreProcess.RunAsync(options,
            [RestoreCommand, output, fixture.RestoredDirectory], token);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restored);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restored, fixture.RestoredDirectory);
        await TruncatedBackupArchiveCold.RunAsync(fixture.RestoredDirectory);
        await CliBackupRestoreAssertions.FilesEqualAsync(source, fixture.SourceDirectory, token);
        await CliBackupRestoreAssertions.FilesEqualAsync(backup, fixture.BackupDirectory, token);
        await TruncatedBackupArchiveAssertions.ArchiveAsync(fixture.ArtifactPath, original, token);
        await TruncatedBackupArchiveAssertions.NoStageAsync(fixture.Root);
        await fixture.AssertSeedRemainsAsync();
    }
}
