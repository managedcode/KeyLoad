namespace KeyLoad.UnitTests.StorageRecovery;

internal sealed class DispatchIdentityPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task RealTemporaryDirectoryFailurePreservesLiveAndDurableIdentityThenRepairs(bool initiallyPaused)
        => DispatchIdentityPublicationFlow.RunAsync(initiallyPaused);
}
