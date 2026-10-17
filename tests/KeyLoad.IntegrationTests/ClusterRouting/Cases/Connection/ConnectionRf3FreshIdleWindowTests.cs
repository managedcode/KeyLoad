namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ConnectionRf3FreshIdleWindowTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task OriginalSdkOrOfficialMcpProducerSettlesBeforeObservedFreshIdleWindowThenNewOwnerReplaysExactReceipt(bool useMcp)
        => ConnectionRf3Scenario.RunAsync((scenario, token) =>
            ConnectionRf3FreshIdleWindowFlow.RunAsync(scenario, useMcp, token),
            TestContext.Current!.Execution.CancellationToken);
}
