namespace KeyLoad.UnitTests.EventStreams;

internal sealed class EventAppendSequenceAuthorityTests
{
    [Test]
    [Arguments(-1L)]
    [Arguments(0L)]
    public Task PersistedSequenceBelowExistingTailRefusesWholeProducerThenRepairReplayAndTwoColdRemainExact(long sequence)
        => EventAppendSequenceAuthorityFlow.RunAsync(sequence, TestContext.Current!.Execution.CancellationToken);
}
