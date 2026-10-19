namespace KeyLoad.UnitTests.Features.Search;

internal sealed class TextWaitThrowingObservationTests
{
    [Test]
    public Task ActualPostReadObserverFailureRetainsIdentityAndCompleteImageThenSameProviderRemainsHealthy()
        => TextWaitThrowingObservationTrial.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
