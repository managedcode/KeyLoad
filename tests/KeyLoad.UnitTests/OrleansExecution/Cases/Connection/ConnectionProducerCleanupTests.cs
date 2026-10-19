namespace KeyLoad.UnitTests.Features.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel(KeyLoad.UnitTests.Features.OrleansExecution.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ConnectionProducerCleanupTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task OriginalProducerCleanupAndAwaitedNotificationRetainPrimaryFailure()
        => ConnectionProducerCleanupFlow.RunAsync(fixture);
}
