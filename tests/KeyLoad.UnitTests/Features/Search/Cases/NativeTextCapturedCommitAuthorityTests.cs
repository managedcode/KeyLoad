namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextCapturedCommitAuthorityTests
{
    [Test]
    public Task OriginalUnconfiguredFtsReadSurvivesCanonicalCommitThenFreshSearchAndSameIdReplay()
        => NativeTextCapturedCommitAuthorityFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
