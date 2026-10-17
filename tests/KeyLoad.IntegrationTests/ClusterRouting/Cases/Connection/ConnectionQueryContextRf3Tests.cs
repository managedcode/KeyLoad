namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ConnectionQueryContextRf3Tests
{
    [Test]
    public Task BlockedSqlLeavesSiblingAdaptersPoliciesAndCursorsIndependentThenReconnectsWithoutEffects()
        => ConnectionRf3Scenario.RunAsync(ConnectionQueryContextFlow.RunAsync,
            TestContext.Current!.Execution.CancellationToken);
}
