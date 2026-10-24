using P = KeyLoad.UnitTests.Authorization.StartedReadPolicyProtocol;

namespace KeyLoad.UnitTests.Authorization;

internal sealed class StartedReadPolicyTests
{
    [Test]
    [Arguments(P.Revoked)]
    [Arguments(P.Expired)]
    [Arguments(P.Cancelled)]
    public Task OriginalStartedReadRequiresCurrentPersistedAuthorityAndSelectedCancellationLeavesSameConnectionHealthy(string variant)
        => StartedReadPolicyFlow.RunAsync(variant);
}
