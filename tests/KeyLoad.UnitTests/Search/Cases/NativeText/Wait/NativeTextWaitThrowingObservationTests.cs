namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextWaitThrowingObservationTests
{
    [Test]
    public Task ActualPostReadObserverFailureRetainsIdentityAndCompleteImageThenSameProviderRemainsHealthy()
        => NativeTextWaitThrowingObservationTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
