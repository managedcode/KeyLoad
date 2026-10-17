namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class TimeSeriesChunkQualificationWholeFlowTests
{
    [Test]
    public Task SameWindowAndSampleIdentitiesStayPartitionScopedAfterRetentionDropReplayAndTwoCold()
        => TimeSeriesChunkQualificationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
