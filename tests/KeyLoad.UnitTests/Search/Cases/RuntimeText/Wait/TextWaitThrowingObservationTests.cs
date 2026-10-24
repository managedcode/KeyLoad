namespace KeyLoad.UnitTests.Search;

internal sealed class TextWaitThrowingObservationTests
{
    [Test]
    public Task ActualPostReadObserverFailureRetainsIdentityAndCompleteImageThenSameProviderRemainsHealthy()
        => TextWaitThrowingObservationTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
