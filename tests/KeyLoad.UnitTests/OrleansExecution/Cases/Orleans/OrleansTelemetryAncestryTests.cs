namespace KeyLoad.UnitTests.Features.OrleansExecution;

[NotInParallel]
internal sealed class OrleansTelemetryAncestryTests
{
    [Test]
    public Task RealSignedWriteSuppressesDeepClientTelemetryThenExportsInclusiveAndShallowSpans()
        => OrleansTelemetryAncestryFlow.RunAsync();
}
