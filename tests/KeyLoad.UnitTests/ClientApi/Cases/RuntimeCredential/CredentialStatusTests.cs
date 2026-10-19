namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class CredentialStatusTests
{
    [Test]
    public Task BooleanStatusRequiresNoDataReplayAndPreservesCredentialDenialAndHealthyProgress()
        => CredentialStatusFlow.RunAsync();
}
