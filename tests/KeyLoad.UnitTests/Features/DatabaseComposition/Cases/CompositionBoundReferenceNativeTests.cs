namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class CompositionBoundReferenceNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativeQueueGraphBoundReferencesRoundTripAndReplayAcrossTwoColdCuts(bool typed)
        => CompositionBoundReferenceNativeFlow.RunAsync(typed, TestContext.Current!.Execution.CancellationToken);
}
