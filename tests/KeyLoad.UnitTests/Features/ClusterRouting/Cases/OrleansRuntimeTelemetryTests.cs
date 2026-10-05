namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsDataSource]
[NotInParallel]
internal sealed class OrleansRuntimeTelemetryTests(RequestCqrsClusterFixture fixture)
{
    [Test]
    public Task AcOrl012RealSignedOperationsExportBoundedPrivateNativeTelemetry()
        => OrleansRuntimeTelemetryWorkflows.RunAsync(fixture);
}
