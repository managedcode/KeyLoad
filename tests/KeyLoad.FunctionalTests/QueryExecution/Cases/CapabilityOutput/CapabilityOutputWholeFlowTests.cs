namespace KeyLoad.FunctionalTests.QueryExecution;

internal sealed class CapabilityOutputWholeFlowTests
{
    [Test]
    [Arguments(1)]
    public Task AdvertisedOutputBoundMatchesSqlAndAstRefusalHealthyAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputFlow.RunAsync(mode, TestContext.Current!.Execution.CancellationToken);
}
