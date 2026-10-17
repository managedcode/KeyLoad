namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class QueryContextSessionRf3Tests
{
    [Test]
    public Task TwoSamePrincipalHeldQueryContextsKeepNormalSiblingCursorsBudgetRefusalSelectedCancellationPoliciesAndTwoColdOwnersIndependent()
        => ConnectionRf3Scenario.RunAsync(QueryContextSessionFlow.RunAsync, TestContext.Current!.Execution.CancellationToken);
}
