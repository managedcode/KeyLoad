namespace KeyLoad.UnitTests.AnalyticsStore;

internal sealed class AnalyticsStorePolicyPublicationTests
{
    [Test]
    public Task PersistedPolicyRevocationAndRestorationFenceOriginalPublicationWithoutGlobalPositionBarrier()
        => AnalyticsStorePolicyPublicationFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
