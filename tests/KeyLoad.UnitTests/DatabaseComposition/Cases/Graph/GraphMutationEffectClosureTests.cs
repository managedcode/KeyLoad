namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed class GraphMutationEffectClosureTests
{
    [Test]
    public Task NativeGraphUpsertBindsBothEndpointLifetimesAndReplaysCold()
        => GraphMutationEffectClosureFlow.RunAsync(false, TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task StagedPutEdgeReplacementAndDeleteKeepOrderedLifetimesAcrossColdReplay()
        => GraphMutationEffectClosureFlow.RunAsync(true, TestContext.Current!.Execution.CancellationToken);
}
