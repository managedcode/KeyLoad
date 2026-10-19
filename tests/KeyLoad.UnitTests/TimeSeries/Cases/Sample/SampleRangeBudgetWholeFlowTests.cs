namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleRangeBudgetWholeFlowTests
{
    [Test]
    [Arguments(SampleRangeBudgetFault.InvertedRange)]
    [Arguments(SampleRangeBudgetFault.ZeroLimit)]
    [Arguments(SampleRangeBudgetFault.ExcessLimit)]
    [Arguments(SampleRangeBudgetFault.CumulativeResponse)]
    public Task AcMp005Series005RefusedRangePreservesAuthorityAcrossColdReplayAndHealthyAppend(
        SampleRangeBudgetFault fault)
        => SampleRangeBudgetWholeFlow.RunAsync(fault);
}
