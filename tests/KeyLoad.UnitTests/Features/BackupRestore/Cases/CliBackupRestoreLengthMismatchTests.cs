using System.Globalization;
using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreLengthMismatchTests
{
    private const string PieceBytesEnvironment = "KEYLOAD_BACKUP__PIECEBYTES";
    private const string PieceBytesConfigurationValue = "1024";
    private const long DeclaredLengthAdjustment = 1;
    private const string BackupCommand = "backup";
    private const string PackCommand = "pack-backup";
    private const string UnpackCommand = "unpack-backup";
    private const string RestoreCommand = "restore";
    private const string FirstMismatchArtifactName = "first-length-mismatch.ctg";
    private const string LastMismatchArtifactName = "last-length-mismatch.ctg";
    private const string FirstRejectedRestoreName = "first-rejected-restore";
    private const string FirstUnpackedDirectoryName = "first-unpacked";
    private const string LastRejectedRestoreName = "last-rejected-restore";
    private const string LastUnpackedDirectoryName = "last-unpacked";
    private const string HealthyUnpackedDirectoryName = "healthy-unpacked";
    private const string StagingPrefix = ".keyload-unpack-";
    private const UnixFileMode PrivateDirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const string BackupManifestFileName = "backup.json";
    private const string IdentityFileName = "identity.json";
    private const string CorruptionCode = "Corruption";
    private const string UnpackLengthMismatchDetail = "The artifact length does not match its catalog.";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";

    [Test]
    public async Task AcBackup006CliLengthMismatchCannotPublishRestorablePartialBackup()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(
            fixture => RunScenarioAsync(options, fixture, cancellationToken), cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task RunScenarioAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, CancellationToken cancellationToken)
    {
        await CreateOriginalBackupAsync(options, fixture, cancellationToken);
        var sourceFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken);
        var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
        var originalArchive = await File.ReadAllBytesAsync(fixture.ArtifactPath, cancellationToken);
        var first = await RunMismatchAsync(options, fixture, lastEntry: false);
        var last = await RunMismatchAsync(options, fixture, lastEntry: true);

        await RestoreCanonicalArtifactAsync(options, fixture, backupFiles, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, cancellationToken);
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
        await AssertArchiveUnchangedAsync(fixture.ArtifactPath, originalArchive, cancellationToken);
        await fixture.AssertSeedRemainsAsync();
        await AssertRejectedRestoreAsync(first);
        await AssertRejectedRestoreAsync(last);
    }

    private static async Task<LengthMismatchAttempt> RunMismatchAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, bool lastEntry)
    {
        var pieceBytes = int.Parse(PieceBytesConfigurationValue, CultureInfo.InvariantCulture);
        var artifactName = lastEntry ? LastMismatchArtifactName : FirstMismatchArtifactName;
        var unpackedName = lastEntry ? LastUnpackedDirectoryName : FirstUnpackedDirectoryName;
        var restoreName = lastEntry ? LastRejectedRestoreName : FirstRejectedRestoreName;
        var mismatchedEntry = lastEntry ? IdentityFileName : BackupManifestFileName;
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var artifactPath = Path.Combine(fixture.Root, artifactName);
        CliBackupRestoreInvalidCatalogArtifact.CreateWithLengthMismatch(fixture.BackupDirectory,
            artifactPath, pieceBytes, mismatchedEntry, DeclaredLengthAdjustment);
        var artifactBytes = await File.ReadAllBytesAsync(artifactPath, cancellationToken);
        var destination = Path.Combine(fixture.Root, restoreName);
        var unpackedDirectory = Path.Combine(fixture.Root, unpackedName);
        UnixFileMode? destinationMode = null;
        if (lastEntry)
        {
            Directory.CreateDirectory(unpackedDirectory);
            if (!OperatingSystem.IsWindows())
            {
                destinationMode = File.GetUnixFileMode(unpackedDirectory);
            }
        }
        var unpack = await RunAsync(options, [UnpackCommand, artifactPath, unpackedDirectory], cancellationToken);
        await CliBackupRestoreAssertions.RejectedProcessAsync(unpack);
        await AssertProblemAsync(unpack.StandardError, CorruptionCode, UnpackLengthMismatchDetail);
        await AssertFailedUnpackDestinationAsync(unpackedDirectory, lastEntry, destinationMode);
        await AssertNoStagingResidueAsync(fixture.Root);
        await AssertArchiveUnchangedAsync(artifactPath, artifactBytes, cancellationToken);

        var restore = await RunAsync(options, [RestoreCommand, unpackedDirectory, destination], cancellationToken);
        await AssertProcessJoinedAsync(restore);
        var destinationExists = Directory.Exists(destination);
        await AssertFailedUnpackDestinationAsync(unpackedDirectory, lastEntry, destinationMode);
        await AssertNoStagingResidueAsync(fixture.Root);
        await AssertArchiveUnchangedAsync(artifactPath, artifactBytes, cancellationToken);
        return new(restore, destinationExists);
    }

    private static async Task AssertFailedUnpackDestinationAsync(string path, bool wasInitiallyEmpty, UnixFileMode? expectedMode)
    {
        await Assert.That(Directory.Exists(path)).IsEqualTo(wasInitiallyEmpty);
        if (wasInitiallyEmpty && Directory.Exists(path))
        {
            await Assert.That(Directory.EnumerateFileSystemEntries(path).Any()).IsFalse();
            if (!OperatingSystem.IsWindows() && expectedMode is not null)
            {
                await Assert.That(File.GetUnixFileMode(path)).IsEqualTo(expectedMode.Value);
            }
        }
    }

    private static async Task AssertNoStagingResidueAsync(string root)
    {
        var leftovers = Directory.EnumerateFileSystemEntries(root)
            .Select(path => Path.GetFileName(path))
            .Where(name => name?.StartsWith(StagingPrefix, StringComparison.Ordinal) == true)
            .ToArray();
        await Assert.That(leftovers).IsEmpty();
    }

    private static async Task CreateOriginalBackupAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, CancellationToken cancellationToken)
    {
        var backup = await RunAsync(options, [BackupCommand, fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
        var pack = await RunAsync(options, [PackCommand, fixture.BackupDirectory, fixture.ArtifactPath],
            PieceBytesOverride(), cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(pack);
    }

    private static async Task RestoreCanonicalArtifactAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, IReadOnlyDictionary<string, byte[]> backupFiles, CancellationToken cancellationToken)
    {
        var healthyUnpacked = Path.Combine(fixture.Root, HealthyUnpackedDirectoryName);
        Directory.CreateDirectory(healthyUnpacked);
        var unpack = await RunAsync(options, [UnpackCommand, fixture.ArtifactPath, healthyUnpacked], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(unpack);
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, healthyUnpacked, cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            await Assert.That(File.GetUnixFileMode(healthyUnpacked)).IsEqualTo(PrivateDirectoryMode);
        }
        var restore = await RunAsync(options, [RestoreCommand, healthyUnpacked, fixture.RestoredDirectory], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore);
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restore, fixture.RestoredDirectory);
    }

    private static async Task AssertRejectedRestoreAsync(LengthMismatchAttempt attempt)
    {
        await Assert.That(attempt.DestinationExists).IsFalse();
        await CliBackupRestoreAssertions.RejectedProcessAsync(attempt.Process);
    }

    private static async Task AssertProblemAsync(string standardError, string expectedCode, string expectedDetail)
    {
        using var problem = JsonDocument.Parse(standardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(expectedCode);
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(expectedDetail);
    }

    private static async Task AssertProcessJoinedAsync(CliBackupRestoreProcessResult result)
    {
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }

    private static async Task AssertArchiveUnchangedAsync(string path, byte[] expected, CancellationToken cancellationToken)
    {
        var actual = await File.ReadAllBytesAsync(path, cancellationToken);
        await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
    }

    private static Dictionary<string, string> PieceBytesOverride()
        => new(StringComparer.Ordinal) { [PieceBytesEnvironment] = PieceBytesConfigurationValue };

    private static Task<CliBackupRestoreProcessResult> RunAsync(IOptions<TestExecutionOptions> options,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => CliBackupRestoreProcess.RunAsync(options, arguments, cancellationToken);

    private static Task<CliBackupRestoreProcessResult> RunAsync(IOptions<TestExecutionOptions> options,
        IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environmentOverrides,
        CancellationToken cancellationToken)
        => CliBackupRestoreProcess.RunAsync(options, arguments, cancellationToken, environmentOverrides);

    private sealed record LengthMismatchAttempt(CliBackupRestoreProcessResult Process, bool DestinationExists);
}
