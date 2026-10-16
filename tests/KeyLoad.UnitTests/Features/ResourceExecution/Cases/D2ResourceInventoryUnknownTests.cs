namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class D2ResourceInventoryUnknownTests
{
    [Test]
    public async Task TwoLegalAuthenticationBootstrapsKeepInventoryUnknownAcrossHealthyWritesReplayAndTwoColdOwners()
        => await D2ResourceInventoryUnknownFlow.ExecuteAsync(TestContext.Current!.Execution.CancellationToken);
}
