namespace KeyLoad.UnitTests.OrleansExecution;

[RequestCqrsHostedDataSource]
[NotInParallel]
internal sealed class OrleansRuntimeTelemetryTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task AcOrl012RealSignedOperationsExportBoundedPrivateTelemetry()
        => OrleansRuntimeTelemetryWorkflows.RunAsync(fixture);
}
