namespace KeyLoad.FunctionalTests.OrleansExecution;

[NotInParallel]
internal sealed class OrleansTelemetryAncestryTests
{
    [Test]
    public Task RealWriteSuppressesDeepClientTelemetryThenExportsInclusiveAndShallowSpans()
        => OrleansTelemetryAncestryFlow.RunAsync();
}
