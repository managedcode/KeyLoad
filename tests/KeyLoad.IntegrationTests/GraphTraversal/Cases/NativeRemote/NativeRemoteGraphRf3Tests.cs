namespace KeyLoad.IntegrationTests.Features.GraphTraversal;

internal sealed class NativeRemoteGraphRf3Tests
{
    [Test]
    public Task GenuineSixOwnerCurrentSourceRightsRefusalRestoresOriginalGraphRequestThenFullReceiptsSurviveTwoColdReopens()
        => NativeRemoteGraphRf3Trial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
