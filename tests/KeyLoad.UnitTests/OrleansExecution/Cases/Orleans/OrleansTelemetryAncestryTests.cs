namespace KeyLoad.UnitTests.OrleansExecution;

[NotInParallel]
internal sealed class OrleansTelemetryAncestryTests
{
    [Test]
    public Task RealWriteSuppressesDeepClientTelemetryThenExportsInclusiveAndShallowSpans()
        => OrleansTelemetryAncestryFlow.RunAsync();
}
