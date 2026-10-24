namespace KeyLoad.UnitTests.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel(KeyLoad.UnitTests.OrleansExecution.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ConnectionProducerCleanupTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task OriginalProducerCleanupAndAwaitedNotificationRetainPrimaryFailure()
        => ConnectionProducerCleanupFlow.RunAsync(fixture);
}
