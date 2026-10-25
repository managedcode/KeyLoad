namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextCapturedCommitAuthorityTests
{
    [Test]
    public Task OriginalUnconfiguredFtsReadSurvivesCanonicalCommitThenFreshSearchAndSameIdReplay()
        => TextCapturedCommitAuthorityFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
