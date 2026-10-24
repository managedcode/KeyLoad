namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class MetamorphicReferenceWholeFlowTests
{
    [Test]
    [Arguments(1, false, 0)]
    [Arguments(1, true, 2)]
    [Arguments(17, false, 2)]
    [Arguments(17, true, 0)]
    public Task GeneratedCommonSemanticsKeepIndependentRowsAcrossFourTransformationsAndAdaptersRepairReplayAndTwoCold(
        int seed, bool descending, int offset)
        => MetamorphicReferenceFlow.RunAsync(seed, descending, offset, TestContext.Current!.Execution.CancellationToken);
}
