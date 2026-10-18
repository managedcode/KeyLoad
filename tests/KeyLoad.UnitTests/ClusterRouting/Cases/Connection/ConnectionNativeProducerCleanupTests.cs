namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsHostedDataSource]
[NotInParallel(KeyLoad.UnitTests.Features.ClusterRouting.RequestCqrsFixtureScheduling.SharedSession)]
internal sealed class ConnectionNativeProducerCleanupTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task OriginalNativeProducerCleanupAndAwaitedNotificationRetainPrimaryFailure()
        => ConnectionNativeProducerCleanupFlow.RunAsync(fixture);
}
