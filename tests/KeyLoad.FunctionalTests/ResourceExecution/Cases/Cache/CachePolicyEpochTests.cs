namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class CachePolicyEpochTests
{
    [Test]
    public Task WarmBytesRespectResourceClassificationAndCurrentPolicyRepairUnderBoundedAdmissionThenTwoReopens()
        => CachePolicyEpochFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
