namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed class BlobSupersessionTests
{
    [Test]
    public async Task AcBlob004SupersessionRetainsOriginalReceiptsAndCurrentBytesThroughTwoColdOwners()
        => await BlobSupersessionTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
