namespace KeyLoad.UnitTests.ClientApi;

internal sealed class CredentialStatusTests
{
    [Test]
    public Task BooleanStatusRequiresNoDataReplayAndPreservesCredentialDenialAndHealthyProgress()
        => CredentialStatusFlow.RunAsync();
}
