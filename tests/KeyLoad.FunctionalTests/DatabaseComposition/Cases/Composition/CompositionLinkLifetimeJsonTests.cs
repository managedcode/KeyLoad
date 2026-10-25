namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed class CompositionLinkLifetimeJsonTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public Task OpaqueQueueLinkLifetimeShapeRefusesWithoutBusinessEffectsThenActualBoundLinkIsHealthy(int malformed)
        => CompositionLinkLifetimeJsonFlow.RunAsync(malformed, TestContext.Current!.Execution.CancellationToken);
}
