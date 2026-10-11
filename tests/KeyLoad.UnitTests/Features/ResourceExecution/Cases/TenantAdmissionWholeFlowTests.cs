namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TenantAdmissionWholeFlowTests
{
    [Test]
    public async Task BusyTenantFairTurnsRefusalCancellationReleaseReplayAndSameRootColdPreserveOriginalOutcomes()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await TenantAdmissionWholeFlow.RunAsync(token);
        await TenantShutdownWholeFlow.RunAsync(token);
    }
}
