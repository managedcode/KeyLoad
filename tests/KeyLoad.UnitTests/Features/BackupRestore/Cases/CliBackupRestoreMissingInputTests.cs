using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreMissingInputTests
{
    private const string BackupCommand = "backup";
    private const string RestoreCommand = "restore";
    private const string ManifestFileName = "backup.json";
    private const string IdentityFileName = "identity.json";
    private const string JournalFileName = "commands.wal";
    private const string ManifestRestoreName = "restored-after-missing-manifest";
    private const string IdentityRestoreName = "restored-after-missing-identity";
    private const string JournalRestoreName = "restored-after-missing-journal";
    private const string RestoreStagingPrefix = ".keyload-restore-";
    private const string CorruptionCode = "Corruption";
    private const string FormatUnsupportedCode = "FormatUnsupported";
    private const string MissingManifestDetail = "The backup manifest is unsupported.";
    private const string MissingRequiredFileDetail = "The backup is missing a required file.";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const int FailedOperationExitCode = 1;
    private static readonly string[] RequiredFileNames = [ManifestFileName, IdentityFileName, JournalFileName];

    [Test]
    public async Task AcBackup002CliMissingRequiredBackupFilesRejectWithoutPublicationAndRestoreAfterRepair()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(
            fixture => RunCasesAsync(options, fixture, cancellationToken), cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task RunCasesAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, CancellationToken cancellationToken)
    {
        var backup = await RunAsync(options, [BackupCommand, fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
        var sourceFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken);
        var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
        foreach (var fileName in RequiredFileNames)
        {
            await AssertMissingInputAsync(options, fixture, fileName, backupFiles, sourceFiles, cancellationToken);
        }
        await fixture.AssertSeedRemainsAsync();
    }

    private static async Task AssertMissingInputAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, string fileName, SortedDictionary<string, byte[]> backupFiles,
        IReadOnlyDictionary<string, byte[]> sourceFiles, CancellationToken cancellationToken)
    {
        var path = Path.Combine(fixture.BackupDirectory, fileName);
        File.Delete(path);
        var destination = Path.Combine(fixture.Root, RestoreName(fileName));
        var rejected = await RunAsync(options, [RestoreCommand, fixture.BackupDirectory, destination], cancellationToken);
        await AssertMissingFileProblemAsync(rejected, fileName);
        await AssertRemainingInputsAsync(fixture.BackupDirectory, backupFiles, fileName, cancellationToken);
        await Assert.That(Directory.Exists(destination)).IsFalse();
        await Assert.That(File.Exists(destination)).IsFalse();
        await AssertNoRestoreStagingResidueAsync(fixture.Root);
        await RestoreOriginalFileAsync(path, backupFiles[fileName], cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
        await RestoreHealthyAsync(options, fixture, fileName, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
        await AssertNoRestoreStagingResidueAsync(fixture.Root);
    }

    private static async Task AssertRemainingInputsAsync(string backupDirectory,
        IReadOnlyDictionary<string, byte[]> backupFiles, string removedFile, CancellationToken cancellationToken)
    {
        var remaining = backupFiles.Where(entry => entry.Key != removedFile)
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        await CliBackupRestoreAssertions.FilesEqualAsync(remaining, backupDirectory, cancellationToken);
    }

    private static async Task AssertNoRestoreStagingResidueAsync(string root)
    {
        var residue = Directory.EnumerateFileSystemEntries(root)
            .Any(path => Path.GetFileName(path)?.StartsWith(RestoreStagingPrefix, StringComparison.Ordinal) == true);
        await Assert.That(residue).IsFalse();
    }

    private static Task RestoreOriginalFileAsync(string path, byte[] originalBytes,
        CancellationToken cancellationToken)
        => File.WriteAllBytesAsync(path, originalBytes, cancellationToken);

    private static async Task RestoreHealthyAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, string fileName, CancellationToken cancellationToken)
    {
        var destination = Path.Combine(fixture.Root, RestoreName(fileName));
        var healthy = await RunAsync(options, [RestoreCommand, fixture.BackupDirectory, destination], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(healthy);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, healthy, destination);
    }

    private static async Task AssertMissingFileProblemAsync(CliBackupRestoreProcessResult result, string fileName)
    {
        await CliBackupRestoreAssertions.RejectedProcessAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(FailedOperationExitCode);
        await Assert.That(result.StandardOutput).IsEmpty();
        using var problem = JsonDocument.Parse(result.StandardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(ExpectedErrorCode(fileName));
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(ExpectedDetail(fileName));
    }

    private static string ExpectedErrorCode(string fileName)
        => fileName == ManifestFileName ? FormatUnsupportedCode : CorruptionCode;

    private static string ExpectedDetail(string fileName)
        => fileName == ManifestFileName ? MissingManifestDetail : MissingRequiredFileDetail;

    private static string RestoreName(string fileName) => fileName switch
    {
        ManifestFileName => ManifestRestoreName,
        IdentityFileName => IdentityRestoreName,
        JournalFileName => JournalRestoreName,
        _ => throw new ArgumentOutOfRangeException(nameof(fileName))
    };

    private static Task<CliBackupRestoreProcessResult> RunAsync(IOptions<TestExecutionOptions> options,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => CliBackupRestoreProcess.RunAsync(options, arguments, cancellationToken);
}
