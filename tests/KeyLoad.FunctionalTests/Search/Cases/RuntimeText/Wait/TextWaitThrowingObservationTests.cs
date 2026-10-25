namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextWaitThrowingObservationTests
{
    [Test]
    public Task ActualPostReadObserverFailureRetainsIdentityAndCompleteImageThenSameProviderRemainsHealthy()
        => TextWaitThrowingObservationTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
