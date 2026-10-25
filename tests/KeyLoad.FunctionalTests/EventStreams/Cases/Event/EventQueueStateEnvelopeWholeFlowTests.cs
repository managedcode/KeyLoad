namespace KeyLoad.FunctionalTests.EventStreams;

internal sealed class EventQueueStateEnvelopeWholeFlowTests
{
    [Test]
    public Task FrozenFeatureKeysAndImmutableClassifiedEnvelopesSurviveActualLifecycleInboxAndTwoColdOwners()
    {
        return EventQueueStateEnvelopeWholeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
    }
}
