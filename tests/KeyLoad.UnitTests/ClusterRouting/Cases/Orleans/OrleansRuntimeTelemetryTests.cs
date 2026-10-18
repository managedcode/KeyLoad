namespace KeyLoad.UnitTests.Features.ClusterRouting;

[RequestCqrsHostedDataSource]
[NotInParallel]
internal sealed class OrleansRuntimeTelemetryTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task AcOrl012RealSignedOperationsExportBoundedPrivateNativeTelemetry()
        => OrleansRuntimeTelemetryWorkflows.RunAsync(fixture);
}
