namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class NativePolicyEpochTests
{
    [Test]
    public Task NativeReadsRespectResourceClassificationAndCurrentPolicyRepairThenTwoReopens()
        => NativePolicyEpochFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
