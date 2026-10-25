using P = KeyLoad.FunctionalTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class SuccessBodyTests
{
    [Test]
    [Arguments(P.NullBody)]
    [Arguments(P.MalformedBody)]
    public Task CompleteUnusableSuccessBodyPreservesCommittedOutcomeConflictAndHealthyRevision(string body)
        => SuccessBodyFlow.RunAsync(body);
}
