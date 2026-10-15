namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class LiveRangeSelectedCancellationTests
{
    [Test]
    public Task AcLiveRangeCancel001LiveAndStagedSelectedCancellationRetainsCanonicalCutAndColdHealth()
        => LiveRangeSelectedCancellationFlow.RunAsync();
}
