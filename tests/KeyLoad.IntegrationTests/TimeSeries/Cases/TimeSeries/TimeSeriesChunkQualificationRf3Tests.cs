namespace KeyLoad.IntegrationTests.Features.TimeSeries;

[NotInParallel]
internal sealed class TimeSeriesChunkQualificationRf3Tests
{
    [Test]
    public Task SameChunkIdentityAcrossAtomicPartitionsKeepsExactValuesOnRetentionDropReplayAndTwoCold()
        => TimeSeriesChunkQualificationRf3Flow.RunAsync();
}
