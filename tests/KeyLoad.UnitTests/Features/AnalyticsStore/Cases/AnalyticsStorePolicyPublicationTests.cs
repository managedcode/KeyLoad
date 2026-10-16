namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal sealed class AnalyticsStorePolicyPublicationTests
{
    [Test]
    public Task PersistedPolicyRevocationAndRestorationFenceOriginalNativePublicationWithoutGlobalPositionBarrier()
        => AnalyticsStorePolicyPublicationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
