namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleSequenceIntegrityTests
{
    [Test]
    [Arguments(-1L)]
    [Arguments(1L)]
    public Task CounterCorruptionRefusesAtomicAppendThenExactRepairCorrectionReplayAndTwoCold(long persistedSequence)
        => SampleSequenceIntegrityFlow.RunAsync(persistedSequence, TestContext.Current!.Execution.CancellationToken);
}
