namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class CapabilityOutputWholeFlowTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public Task AdvertisedOutputBoundMatchesSqlAndAstRefusalHealthyAuthorityReplayAndTwoCold(int mode)
        => CapabilityOutputFlow.RunAsync(mode, TestContext.Current!.Execution.CancellationToken);
}
