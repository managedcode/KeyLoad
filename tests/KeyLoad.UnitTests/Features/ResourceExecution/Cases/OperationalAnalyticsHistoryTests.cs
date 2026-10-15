namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class OperationalAnalyticsHistoryTests
{
    [Test]
    [NotInParallel]
    public Task ActualJoinedDerivedHistoryDeletionLeavesAbsentSamplesThenFreshNativeCutAndTwoColdConserveOriginalCanonicalState()
        => OperationalAnalyticsHistoryFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
