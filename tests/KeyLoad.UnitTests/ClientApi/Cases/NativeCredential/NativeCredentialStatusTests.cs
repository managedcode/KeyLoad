namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class NativeCredentialStatusTests
{
    [Test]
    public Task NativeBooleanStatusRequiresNoDataReplayAndPreservesCredentialDenialAndHealthyProgress()
        => NativeCredentialStatusFlow.RunAsync();
}
