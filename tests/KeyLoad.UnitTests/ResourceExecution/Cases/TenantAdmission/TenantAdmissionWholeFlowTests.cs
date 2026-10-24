namespace KeyLoad.UnitTests.ResourceExecution;

internal sealed class TenantAdmissionWholeFlowTests
{
    [Test]
    public async Task TenantBoundsRefusalCancellationReleaseReplayAndSameRootColdPreserveOriginalOutcomes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await TenantAdmissionWholeFlow.RunAsync(token);
        await TenantShutdownWholeFlow.RunAsync(token);
    }
}
