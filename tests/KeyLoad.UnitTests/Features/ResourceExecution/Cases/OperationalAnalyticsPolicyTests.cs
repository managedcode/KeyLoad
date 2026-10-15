namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class OperationalAnalyticsPolicyTests
{
    [Test]
    public Task InvalidTypedPolicyAcquisitionPreservesCanonicalThenHealthySamplingSurvivesTwoJoinedColdReopens()
        => OperationalAnalyticsPolicyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
