namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed class CompositionBoundReferenceTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task QueueGraphBoundReferencesRoundTripAndReplayAcrossTwoColdCuts(bool typed)
        => CompositionBoundReferenceFlow.RunAsync(typed, TestContext.Current!.Execution.CancellationToken);
}
