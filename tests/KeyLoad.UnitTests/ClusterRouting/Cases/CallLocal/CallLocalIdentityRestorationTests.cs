namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class CallLocalIdentityRestorationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ConcurrentNativeCommandsRestoreDistinctPriorContextsAfterCancellation(bool firstPresentNull)
        => CallLocalIdentityRestorationFlow.RunAsync(firstPresentNull, TestContext.Current!.Execution.CancellationToken);
}
