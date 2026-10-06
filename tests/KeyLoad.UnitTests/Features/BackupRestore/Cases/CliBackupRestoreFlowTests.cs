using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreFlowTests
{
    private const string FrameBudgetEnvironment = "KEYLOAD_STORAGE__MAXFRAMEBYTES";
    private const string InvalidFrameBudget = "0";

    [Test]
    public async Task AcBackup001CliCommandsCopyUnpackAndRestoreCommittedStore()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            await BackupAndPackAsync(options, fixture, cancellationToken);
            var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
            var inspect = await RunAsync(options, ["inspect-artifact", fixture.ArtifactPath], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(inspect);
            await CliBackupRestoreAssertions.InspectionAsync(inspect.StandardOutput, backupFiles);
            var copy = await RunAsync(options, ["copy-artifact", fixture.ArtifactPath, fixture.CopiedArtifactDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(copy);
            var archive = await File.ReadAllBytesAsync(fixture.ArtifactPath, cancellationToken);
            var copied = await File.ReadAllBytesAsync(fixture.CopiedArtifactPath, cancellationToken);
            await Assert.That(copied.AsSpan().SequenceEqual(archive)).IsTrue();
            var unpack = await RunAsync(options, ["unpack-backup", fixture.CopiedArtifactPath, fixture.UnpackedDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(unpack);
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.UnpackedDirectory, cancellationToken);
            var restore = await RunAsync(options, ["restore", fixture.UnpackedDirectory, fixture.RestoredDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore);
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restore, fixture.RestoredDirectory);
        }, cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task AcBackup002CliRestoreRejectsNonemptyDestinationWithoutChangingEitherStore()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            var backup = await RunAsync(options, ["backup", fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
            var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
            Directory.CreateDirectory(fixture.ExistingDestination);
            var preservedPath = Path.Combine(fixture.ExistingDestination, CliBackupRestoreFixture.PreservedFileName);
            await File.WriteAllTextAsync(preservedPath, CliBackupRestoreFixture.PreservedFileContents, cancellationToken);
            var rejected = await RunAsync(options, ["restore", fixture.BackupDirectory, fixture.ExistingDestination], cancellationToken);
            await CliBackupRestoreAssertions.RejectedProcessAsync(rejected);
            await Assert.That(await File.ReadAllTextAsync(preservedPath, cancellationToken)).IsEqualTo(CliBackupRestoreFixture.PreservedFileContents);
            var destinationNames = Directory.EnumerateFileSystemEntries(fixture.ExistingDestination)
                .Select(static path => Path.GetFileName(path)).ToArray();
            await Assert.That(destinationNames.Length == 1
                && destinationNames[0] == CliBackupRestoreFixture.PreservedFileName).IsTrue();
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
            await fixture.AssertSeedRemainsAsync();
            var healthy = await RunAsync(options, ["restore", fixture.BackupDirectory, fixture.RestoredDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(healthy);
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, healthy, fixture.RestoredDirectory);
        }, cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    [Test]
    public async Task AcBackup001CliRejectsInvalidStorageBudgetBeforeOpeningSourceThenRestoresHealthyBackup()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            var sourceFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken);
            var rejected = await CliBackupRestoreProcess.RunAsync(options,
                ["backup", fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken,
                new Dictionary<string, string> { [FrameBudgetEnvironment] = InvalidFrameBudget });
            await CliBackupRestoreAssertions.RejectedProcessAsync(rejected);
            await Assert.That(rejected.StandardError.Contains(ZoneTreeStorageExecutionOptions.ValidationMessage,
                StringComparison.Ordinal)).IsTrue();
            await Assert.That(Directory.Exists(fixture.BackupDirectory)).IsFalse();
            await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, cancellationToken);
            await fixture.AssertSeedRemainsAsync();

            var backup = await RunAsync(options, ["backup", fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
            var restored = await RunAsync(options, ["restore", fixture.BackupDirectory, fixture.RestoredDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(restored);
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restored, fixture.RestoredDirectory);
        }, cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task BackupAndPackAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, CancellationToken cancellationToken)
    {
        var backup = await RunAsync(options, ["backup", fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
        var pack = await RunAsync(options, ["pack-backup", fixture.BackupDirectory, fixture.ArtifactPath], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(pack);
    }

    private static Task<CliBackupRestoreProcessResult> RunAsync(IOptions<TestExecutionOptions> options,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => CliBackupRestoreProcess.RunAsync(options, arguments, cancellationToken);
}
