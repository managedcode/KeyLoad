using P = KeyLoad.UnitTests.Features.ClientApi.SuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class SuccessBodyTests
{
    [Test]
    [Arguments(P.NullBody)]
    [Arguments(P.MalformedBody)]
    public Task CompleteUnusableSuccessBodyPreservesCommittedOutcomeConflictAndHealthyRevision(string body)
        => SuccessBodyFlow.RunAsync(body);
}
