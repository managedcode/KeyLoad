using System.Globalization;

namespace KeyLoad.AppHost.Features.ClientApi;

/// <summary>Publishes the native multiplexed listener alongside each node's original HTTP endpoint.</summary>
internal static class ClusterConnectionEndpointResources
{
    private const string Http2Endpoint = "http2";
    private const string EnableEnvironment = "KeyLoad__ServerExecution__EnableHttp2";
    private const string PortEnvironment = "KeyLoad__ServerExecution__Http2Port";
    private const string Enabled = "true";
    private const int Http2Port = 8081;

    internal static void Apply(IResourceBuilder<ContainerResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.WithHttpEndpoint(targetPort: Http2Port, name: Http2Endpoint, isProxied: false)
            .WithEnvironment(EnableEnvironment, Enabled)
            .WithEnvironment(PortEnvironment, Http2Port.ToString(CultureInfo.InvariantCulture));
    }
}
