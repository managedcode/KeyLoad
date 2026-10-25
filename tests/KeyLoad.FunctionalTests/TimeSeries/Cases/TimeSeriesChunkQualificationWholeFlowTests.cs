namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class TimeSeriesChunkQualificationWholeFlowTests
{
    [Test]
    public Task SameWindowAndSampleIdentitiesStayPartitionScopedAfterRetentionDropReplayAndTwoCold()
        => TimeSeriesChunkQualificationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
