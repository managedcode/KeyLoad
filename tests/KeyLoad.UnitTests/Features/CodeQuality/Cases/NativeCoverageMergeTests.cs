using System.Text.Json;
using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class NativeCoverageMergeTests
{
    private const string ProofKindProperty = "proofKind";
    private const string RepeatedInputProperty = "repeatedInputInvariant";
    private const string ProductQualificationProperty = "productQualification";
    [Test]
    public async Task ActualCliBackupRestoreCoverageMergesWithRepeatedInputInvariantAndPreservedState()
    {
        var options = NativeCoverageMergeProcess.CaptureOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var root = await CliBackupRestoreFixture.RunAsync(async fixture =>
        {
            var coverageRoot = Path.Combine(fixture.Root, "native-coverage");
            Directory.CreateDirectory(coverageRoot);
            var backupReport = Path.Combine(coverageRoot, "backup.coverage");
            var packReport = Path.Combine(coverageRoot, "pack.coverage");
            var restoreReport = Path.Combine(coverageRoot, "restore.coverage");
            var backup = await NativeCoverageMergeProcess.CollectCliAsync(options,
                ["backup", fixture.SourceDirectory, fixture.BackupDirectory], backupReport, cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(backup.AsCliResult());
            var backupFiles = await CliBackupRestoreAssertions.CaptureFilesAsync(fixture.BackupDirectory, cancellationToken);
            var pack = await NativeCoverageMergeProcess.CollectCliAsync(options,
                ["pack-backup", fixture.BackupDirectory, fixture.ArtifactPath], packReport, cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(pack.AsCliResult());
            var inspect = await CliBackupRestoreProcess.RunAsync(CliBackupRestoreProcess.CaptureExecutionOptions(),
                ["inspect-artifact", fixture.ArtifactPath], cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(inspect);
            await CliBackupRestoreAssertions.InspectionAsync(inspect.StandardOutput, backupFiles);
            var restore = await NativeCoverageMergeProcess.CollectCliAsync(options,
                ["restore", fixture.BackupDirectory, fixture.RestoredDirectory], restoreReport, cancellationToken);
            await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore.AsCliResult());
            await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restore.AsCliResult(), fixture.RestoredDirectory);
            var reports = new[] { backupReport, packReport, restoreReport };
            var hashes = reports.Select(path => NativeCoverageMergeProcess.HashFile(path, options.Coverage)).ToArray();
            var output = Path.Combine(coverageRoot, "merge-output");
            using var merged = await NativeCoverageMergeProcess.MergeToolingReportsAsync(options, output, reports, cancellationToken);
            await Assert.That(merged.RootElement.GetProperty(ProofKindProperty).GetString()).IsEqualTo("native-tooling-only");
            await Assert.That(merged.RootElement.GetProperty(RepeatedInputProperty).GetBoolean()).IsTrue();
            await Assert.That(merged.RootElement.GetProperty(ProductQualificationProperty).GetBoolean()).IsFalse();
            await Assert.That(File.Exists(Path.Combine(output, "merged.coverage"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(output, "merged.xml"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(output, "merged.cobertura"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(output, "repeated-input-control.xml"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(output, "repeated-input-control.coverage"))).IsTrue();
            var proofFile = await File.ReadAllTextAsync(Path.Combine(output, "tooling-proof.json"), cancellationToken);
            using var persistedProof = JsonDocument.Parse(proofFile);
            await NativeCoverageLineUnionAssertions.AssertMatchesNativeToolingProofAsync(
                Path.Combine(output, "merged.cobertura"), options.Coverage.MaximumReportBytes,
                persistedProof.RootElement);
            await Assert.That(persistedProof.RootElement.GetProperty(ProductQualificationProperty).GetBoolean()).IsFalse();
            await Assert.That(reports.Select(path => NativeCoverageMergeProcess.HashFile(path, options.Coverage)).SequenceEqual(hashes)).IsTrue();
            await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
        }, cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}
