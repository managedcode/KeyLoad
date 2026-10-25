namespace KeyLoad.FunctionalTests.Search;

internal sealed class Bm25RestoredEligibilityTests
{
    [Test]
    public Task ActualRestoredHeadsDeclineReuseUntilCurrentAuthorizationAndFreshPublicationThenReplayAcrossBothReopens()
        => Bm25RestoredEligibilityTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
