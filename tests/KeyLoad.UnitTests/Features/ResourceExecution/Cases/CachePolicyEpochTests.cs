namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CachePolicyEpochTests
{
    [Test]
    public Task WarmNativeBytesRespectResourceClassificationAndCurrentPolicyRepairUnderBoundedAdmissionThenTwoReopens()
        => CachePolicyEpochFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
