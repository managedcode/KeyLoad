namespace KeyLoad.ClusterTests.TimeSeries;

[NotInParallel]
internal sealed class TimeSeriesChunkQualificationClusterTests
{
    [Test]
    public Task SameChunkIdentityAcrossAtomicPartitionsKeepsExactValuesOnRetentionDropReplayAndTwoCold()
        => TimeSeriesChunkQualificationClusterFlow.RunAsync();
}
