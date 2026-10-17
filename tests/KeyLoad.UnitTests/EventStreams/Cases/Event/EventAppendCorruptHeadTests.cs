namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class EventAppendCorruptHeadTests
{
    [Test]
    [Arguments(-1L, 1L)]
    [Arguments(1L, 0L)]
    [Arguments(1L, 3L)]
    public Task ActualInvalidNativeHeadRefusesWholeProducerAndRetainsOriginalFailureThenHealthyAppend(long tail, long first)
        => EventAppendCorruptHeadFlow.RunAsync(tail, first, TestContext.Current!.Execution.CancellationToken);
}
