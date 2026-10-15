using P = KeyLoad.UnitTests.Features.ClientApi.NativeSuccessBodyProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeSuccessBodyTests
{
    [Test]
    [Arguments(P.NullBody)]
    [Arguments(P.MalformedBody)]
    public Task CompleteUnusableNativeSuccessBodyPreservesCommittedOutcomeConflictAndHealthyRevision(string body)
        => NativeSuccessBodyFlow.RunAsync(body);
}
