using KeyLoad.UnitTests.BackupRestore;

namespace KeyLoad.UnitTests.CodeQuality;

internal sealed class CoverageMergeTests
{
    [Test]
    public async Task ActualCliBackupRestoreCoverageMergesWithRepeatedInputInvariantAndPreservedState()
    {
        var options = CoverageMergeProcess.CaptureOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var context = TestContext.Current!;
        var root = await CliBackupRestoreFixture.RunAsync(fixture =>
            CoverageMergeEvidenceRetention.RunBeforeFixtureCleanupAsync(
                async () =>
                {
                    await CoverageMergeScenario.RunAsync(fixture, options, cancellationToken);
                    await CoverageProductEvidenceRejection.RunIfRequiredAsync(options,
                        TestContext.ResultsDirectory, cancellationToken);
                },
                Path.Combine(fixture.Root, CoverageMergeScenario.CoverageDirectoryName),
                TestContext.ResultsDirectory, context.Output, options.Coverage), cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}
