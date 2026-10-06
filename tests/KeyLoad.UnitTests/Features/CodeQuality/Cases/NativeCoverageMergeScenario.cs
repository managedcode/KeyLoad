using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeScenario
{
    internal const string CoverageDirectoryName = "native-coverage";
    private const string ProofKindProperty = "proofKind";
    private const string RepeatedInputProperty = "repeatedInputInvariant";
    private const string ProductQualificationProperty = "productQualification";
    private const string BackupReportName = "backup.coverage";
    private const string PackReportName = "pack.coverage";
    private const string RestoreReportName = "restore.coverage";
    private const string MergeOutputName = "merge-output";

    internal static async Task RunAsync(CliBackupRestoreFixture fixture,
        NativeCoverageMergeProcess.ToolingOptions options, CancellationToken cancellationToken)
    {
        var coverageRoot = Path.Combine(fixture.Root, CoverageDirectoryName);
        Directory.CreateDirectory(coverageRoot);
        var backupReport = Path.Combine(coverageRoot, BackupReportName);
        var packReport = Path.Combine(coverageRoot, PackReportName);
        var restoreReport = Path.Combine(coverageRoot, RestoreReportName);
        var backupFiles = await RunBackupAndPackAsync(fixture, options, backupReport, packReport, cancellationToken);
        await RunRestoreAsync(fixture, options, restoreReport, cancellationToken);
        await MergeAndAssertAsync(fixture, options, coverageRoot,
            [backupReport, packReport, restoreReport], backupFiles, cancellationToken);
    }

    private static async Task<SortedDictionary<string, byte[]>> RunBackupAndPackAsync(CliBackupRestoreFixture fixture,
        NativeCoverageMergeProcess.ToolingOptions options, string backupReport,
        string packReport, CancellationToken cancellationToken)
    {
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
        return backupFiles;
    }

    private static async Task RunRestoreAsync(CliBackupRestoreFixture fixture,
        NativeCoverageMergeProcess.ToolingOptions options, string restoreReport, CancellationToken cancellationToken)
    {
        var restore = await NativeCoverageMergeProcess.CollectCliAsync(options,
            ["restore", fixture.BackupDirectory, fixture.RestoredDirectory], restoreReport, cancellationToken);
        await CliBackupRestoreAssertions.SuccessfulProcessAsync(restore.AsCliResult());
        await CliBackupRestoreAssertions.RestoredStoreAsync(fixture, restore.AsCliResult(), fixture.RestoredDirectory);
    }

    private static async Task MergeAndAssertAsync(CliBackupRestoreFixture fixture,
        NativeCoverageMergeProcess.ToolingOptions options, string coverageRoot, IReadOnlyList<string> reports,
        IReadOnlyDictionary<string, byte[]> backupFiles, CancellationToken cancellationToken)
    {
        var hashes = reports.Select(path => NativeCoverageMergeProcess.HashFile(path, options.Coverage)).ToArray();
        var output = Path.Combine(coverageRoot, MergeOutputName);
        using var merged = await NativeCoverageMergeProcess.MergeToolingReportsAsync(options, output, reports, cancellationToken);
        await Assert.That(merged.RootElement.GetProperty(ProofKindProperty).GetString()).IsEqualTo("native-tooling-only");
        await Assert.That(merged.RootElement.GetProperty(RepeatedInputProperty).GetBoolean()).IsTrue();
        await Assert.That(merged.RootElement.GetProperty(ProductQualificationProperty).GetBoolean()).IsFalse();
        await AssertMergeOutputsAsync(output);
        var proofFile = Path.Combine(output, "tooling-proof.json");
        using var persistedProof = await NativeCoverageMergeEvidenceReader.ReadProofAsync(
            proofFile, options.Coverage, cancellationToken);
        await NativeCoverageLineUnionAssertions.AssertMatchesNativeToolingProofAsync(
            Path.Combine(output, "merged.cobertura"), options.Coverage.MaximumReportBytes, persistedProof.RootElement);
        await Assert.That(persistedProof.RootElement.GetProperty(ProductQualificationProperty).GetBoolean()).IsFalse();
        await Assert.That(reports.Select(path => NativeCoverageMergeProcess.HashFile(path, options.Coverage)).SequenceEqual(hashes)).IsTrue();
        await CliBackupRestoreAssertions.FilesEqualAsync(backupFiles, fixture.BackupDirectory, cancellationToken);
    }

    private static async Task AssertMergeOutputsAsync(string output)
    {
        await Assert.That(File.Exists(Path.Combine(output, "merged.coverage"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "merged.xml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "merged.cobertura"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "repeated-input-control.xml"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(output, "repeated-input-control.coverage"))).IsTrue();
    }
}
