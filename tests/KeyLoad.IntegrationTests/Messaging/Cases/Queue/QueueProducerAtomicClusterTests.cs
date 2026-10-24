namespace KeyLoad.IntegrationTests.Messaging;

internal sealed class QueueProducerAtomicClusterTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualMixedProducerBatchRefusalsAndOriginalResponseCancellationSurviveColdHealthyContinuation(bool cancelAfterResponse)
        => QueueProducerClusterTrial.RunAsync(cancelAfterResponse);
}
