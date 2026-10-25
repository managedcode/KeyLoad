namespace KeyLoad.FunctionalTests.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel(KeyLoad.FunctionalTests.OrleansExecution.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ConnectionProducerCleanupTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task OriginalProducerCleanupAndAwaitedNotificationRetainPrimaryFailure()
        => ConnectionProducerCleanupFlow.RunAsync(fixture);
}
