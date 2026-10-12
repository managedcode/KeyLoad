namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobRangePolicyColdTests
{
    [Test]
    public Task AcBlob003RevokedRangeRemainsDeniedColdThenRestoredPolicyReadsExactVersion()
        => BlobRangePolicyColdTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
