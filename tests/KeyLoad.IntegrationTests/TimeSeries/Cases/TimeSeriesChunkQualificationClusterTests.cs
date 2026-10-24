namespace KeyLoad.IntegrationTests.TimeSeries;

[NotInParallel]
internal sealed class TimeSeriesChunkQualificationClusterTests
{
    [Test]
    public Task SameChunkIdentityAcrossAtomicPartitionsKeepsExactValuesOnRetentionDropReplayAndTwoCold()
        => TimeSeriesChunkQualificationClusterFlow.RunAsync();
}
