namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionFixtureConcurrencyTests
{
    [Test]
    public Task IndependentlyOwnedNativeConnectionsReachBothOriginalBarriersAndSettle()
        => ConnectionNativeScenario.RunAsync(left => ConnectionNativeScenario.RunAsync(
            right => ConnectionFixtureConcurrencyFlow.RunAsync(left, right)));
}
