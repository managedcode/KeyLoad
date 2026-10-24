using P = KeyLoad.UnitTests.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.UnitTests.ClientApi;

internal sealed class SuccessBodyTests
{
    [Test]
    [Arguments(P.NullBody)]
    [Arguments(P.MalformedBody)]
    public Task CompleteUnusableSuccessBodyPreservesCommittedOutcomeConflictAndHealthyRevision(string body)
        => SuccessBodyFlow.RunAsync(body);
}
