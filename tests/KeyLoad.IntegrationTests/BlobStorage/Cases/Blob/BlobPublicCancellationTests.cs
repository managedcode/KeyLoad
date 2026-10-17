namespace KeyLoad.IntegrationTests.Features.BlobStorage;

[NotInParallel]
internal sealed class BlobPublicCancellationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task BeforeSubmitPartAndCompleteCancellationJoinsOriginalProducerThenExactRetryPublishesHealthyBlob(bool useMcp)
        => BlobPublicCancellationOwner.RunAsync(useMcp, TestContext.Current!.Execution.CancellationToken);
}
