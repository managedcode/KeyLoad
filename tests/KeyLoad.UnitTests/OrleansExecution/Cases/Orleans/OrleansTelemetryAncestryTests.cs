namespace KeyLoad.UnitTests.Features.OrleansExecution;

[NotInParallel]
internal sealed class OrleansTelemetryAncestryTests
{
    [Test]
    public Task RealWriteSuppressesDeepClientTelemetryThenExportsInclusiveAndShallowSpans()
        => OrleansTelemetryAncestryFlow.RunAsync();
}
