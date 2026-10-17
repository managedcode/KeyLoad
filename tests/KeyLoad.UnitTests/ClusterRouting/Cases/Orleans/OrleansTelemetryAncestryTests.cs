namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class OrleansTelemetryAncestryTests
{
    [Test]
    public Task RealSignedWriteSuppressesDeepNativeClientTelemetryThenExportsInclusiveAndShallowSpans()
        => OrleansTelemetryAncestryFlow.RunAsync();
}
