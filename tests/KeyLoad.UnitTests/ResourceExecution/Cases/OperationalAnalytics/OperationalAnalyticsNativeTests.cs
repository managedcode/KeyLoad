namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class OperationalAnalyticsNativeTests
{
    [Test]
    [NotInParallel]
    public Task ActualNativePositionsAndFixedBacklogRefuseSecondOwnerThenTwoJoinedColdReopensConserveReportsAndCanonicalReceipts()
        => OperationalAnalyticsNativeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
