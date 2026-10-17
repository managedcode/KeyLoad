namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel(KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ConnectionNativeProducerCleanupTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public Task OriginalNativeProducerCleanupAndAwaitedNotificationRetainPrimaryFailure()
        => ConnectionNativeProducerCleanupFlow.RunAsync(fixture);
}
