namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed class CompositionReverseFinalImageTests
{
    [Test]
    public Task ZeroEdgeReverseThenDeleteCommitsBoundAuthorityButRefusesPublicationAcrossTwoColdCuts()
        => CompositionReverseFinalImageFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
