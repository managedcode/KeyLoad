namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueProducerAtomicRf3Tests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualMixedProducerBatchRefusalsAndOriginalResponseCancellationSurviveColdHealthyContinuation(bool cancelAfterResponse)
        => QueueProducerRf3Trial.RunAsync(cancelAfterResponse);
}
