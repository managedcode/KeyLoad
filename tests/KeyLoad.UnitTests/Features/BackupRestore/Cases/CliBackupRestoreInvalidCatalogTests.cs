using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class CliBackupRestoreInvalidCatalogTests
{
    private const int InvalidCatalogPieceBytes = 1_048_576;
    private const string InvalidCatalogDetail = "The artifact is not a canonical KeyLoad backup.";
    private const string InvalidCatalogErrorCode = "Validation";
    private const string ErrorCodeProperty = "errorCode";
    private const string DetailProperty = "detail";
    private const string InvalidArtifactFileName = "noncanonical.ctg";
    private const string BackupCommand = "backup";
    private const string PackCommand = "pack-backup";
    private const string UnpackCommand = "unpack-backup";
    private const string RestoreCommand = "restore";

    [Test]
    public async Task AcBackup002CliRejectsNoncanonicalNativeArtifactThenRestoresOriginal()
    {
        var options = CliBackupRestoreProcess.CaptureExecutionOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            await CreateBackupAndArtifactAsync(options, fixture, cancellationToken);
            var sourceFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.SourceDirectory, cancellationToken);
            var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
            var canonicalBytes = await File.ReadAllBytesAsync(fixture.ArtifactPath, cancellationToken);
            var invalidPath = Path.Combine(fixture.Root, InvalidArtifactFileName);
            CliBackupRestoreInvalidCatalogArtifact.Create(fixture.BackupDirectory, invalidPath, InvalidCatalogPieceBytes);
            var invalidBytes = await File.ReadAllBytesAsync(invalidPath, cancellationToken);

            var rejected = await RunAsync(options, [UnpackCommand, invalidPath, fixture.UnpackedDirectory], cancellationToken);
            await CliBackupRestoreAssertions.RejectedProcessAsync(rejected);
            await AssertInvalidCatalogProblemAsync(rejected.StandardError);
            await Assert.That(Directory.Exists(fixture.UnpackedDirectory)).IsFalse();
            await Assert.That((await File.ReadAllBytesAsync(invalidPath, cancellationToken)).AsSpan().SequenceEqual(invalidBytes)).IsTrue();
            await Assert.That((await File.ReadAllBytesAsync(fixture.ArtifactPath, cancellationToken)).AsSpan().SequenceEqual(canonicalBytes)).IsTrue();
            await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, cancellationToken);
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);

            var unpacked = await RunAsync(options, [UnpackCommand, fixture.ArtifactPath, fixture.UnpackedDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(unpacked);
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.UnpackedDirectory, cancellationToken);
            var restored = await RunAsync(options, [RestoreCommand, fixture.UnpackedDirectory, fixture.RestoredDirectory], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(restored);
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restored, fixture.RestoredDirectory);
            await CliBackupRestoreAssertions.FilesEqualAsync(sourceFiles, fixture.SourceDirectory, cancellationToken);
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
            await fixture.AssertSeedRemainsAsync();
        }, cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }

    private static async Task CreateBackupAndArtifactAsync(IOptions<TestExecutionOptions> options,
        CliBackupRestoreFixture fixture, CancellationToken cancellationToken)
    {
        var backup = await RunAsync(options, [BackupCommand, fixture.SourceDirectory, fixture.BackupDirectory], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup);
        var pack = await RunAsync(options, [PackCommand, fixture.BackupDirectory, fixture.ArtifactPath], cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(pack);
    }

    private static async Task AssertInvalidCatalogProblemAsync(string standardError)
    {
        using var problem = JsonDocument.Parse(standardError);
        await Assert.That(problem.RootElement.GetProperty(ErrorCodeProperty).GetString()).IsEqualTo(InvalidCatalogErrorCode);
        await Assert.That(problem.RootElement.GetProperty(DetailProperty).GetString()).IsEqualTo(InvalidCatalogDetail);
    }

    private static Task<CliBackupRestoreProcessResult> RunAsync(IOptions<TestExecutionOptions> options,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => CliBackupRestoreProcess.RunAsync(options, arguments, cancellationToken);
}
