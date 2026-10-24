namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class SampleChunkExplicitMinimumWholeFlowTests
{
    [Test]
    public async Task OriginalMinimumScopeRefusalFreshPermissionRepairReceiptsAndTwoColdOwners()
        => await SampleChunkExplicitMinimumUnitFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
