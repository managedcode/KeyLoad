namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class LiveRangeSelectedCancellationTests
{
    [Test]
    public Task AcLiveRangeCancel001LiveAndStagedSelectedCancellationRetainsCanonicalCutAndColdHealth()
        => LiveRangeSelectedCancellationFlow.RunAsync();
}
