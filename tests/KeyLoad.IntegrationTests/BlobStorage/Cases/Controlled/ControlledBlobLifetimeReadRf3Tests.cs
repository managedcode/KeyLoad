namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[NotInParallel]
internal sealed class ControlledBlobLifetimeReadRf3Tests
{
    [Test]
    public Task HeldNativeRepliesRequireFreshSourceAuthorityAndSettleBeforeTwoColdBlobReadWaves()
        => ControlledBlobLifetimeRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
