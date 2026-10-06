using KeyLoad.UnitTests.Features.BackupRestore;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal sealed class NativeCoverageMergeTests
{
    [Test]
    public async Task ActualCliBackupRestoreCoverageMergesWithRepeatedInputInvariantAndPreservedState()
    {
        var options = NativeCoverageMergeProcess.CaptureOptions();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var context = TestContext.Current!;
        var root = await CliBackupRestoreFixture.RunAsync(fixture =>
            NativeCoverageMergeEvidenceRetention.RunBeforeFixtureCleanupAsync(
                () => NativeCoverageMergeScenario.RunAsync(fixture, options, cancellationToken),
                Path.Combine(fixture.Root, NativeCoverageMergeScenario.CoverageDirectoryName),
                TestContext.ResultsDirectory, context.Output, options.Coverage), cancellationToken);
        await Assert.That(Directory.Exists(root)).IsFalse();
    }
}
