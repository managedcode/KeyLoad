using Aspire.Hosting.ApplicationModel;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.TestInfrastructure.Cases;

internal static class NativeLoggerModelEndpointAssertions
{
    private const string HttpEndpointName = "http";
    private const string SiloEndpointName = "silo";
    private const string Http2EndpointName = "http2";
    private const string HttpScheme = "http";
    private const string SiloScheme = "tcp";
    private const int HttpTargetPort = 8080;
    private const int SiloTargetPort = 11111;
    private const int Http2TargetPort = 8081;

    internal static async Task VerifyAsync(ContainerResource node)
    {
        var endpoints = node.Annotations.OfType<EndpointAnnotation>().ToArray();
        await Assert.That(endpoints.Select(endpoint => endpoint.Name).ToArray())
            .IsEquivalentTo([HttpEndpointName, SiloEndpointName, Http2EndpointName], CollectionOrdering.Matching);
        var http = endpoints.Single(endpoint => endpoint.Name == HttpEndpointName);
        await Assert.That(http.Port).IsEqualTo(HttpTargetPort);
        await Assert.That(http.TargetPort).IsEqualTo(HttpTargetPort);
        await Assert.That(http.IsProxied).IsFalse();
        var silo = endpoints.Single(endpoint => endpoint.Name == SiloEndpointName);
        await Assert.That(silo.UriScheme).IsEqualTo(SiloScheme);
        await Assert.That(silo.TargetPort).IsEqualTo(SiloTargetPort);
        await Assert.That(silo.IsExternal).IsFalse();
        await Assert.That(silo.IsProxied).IsFalse();
        var http2 = endpoints.Single(endpoint => endpoint.Name == Http2EndpointName);
        await Assert.That(http2.UriScheme).IsEqualTo(HttpScheme);
        await Assert.That(http2.Port).IsEqualTo(Http2TargetPort);
        await Assert.That(http2.TargetPort).IsEqualTo(Http2TargetPort);
        await Assert.That(http2.IsExternal).IsFalse();
        await Assert.That(http2.IsProxied).IsFalse();
    }
}
