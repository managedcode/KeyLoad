namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class CapabilityOutputWholeFlowTests
{
    [Test]
    [Arguments(1)]
    public Task AdvertisedOutputBoundMatchesSqlAndAstRefusalHealthyAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputFlow.RunAsync(mode, TestContext.Current!.Execution.CancellationToken);
}
