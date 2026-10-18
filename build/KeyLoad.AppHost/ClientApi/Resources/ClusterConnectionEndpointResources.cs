namespace KeyLoad.AppHost.Features.ClientApi;

/// <summary>Publishes an allocated native HTTP/2 listener for each server process.</summary>
internal static class ClusterConnectionEndpointResources
{
    private const string Http2Endpoint = "http2";
    private const string EnableEnvironment = "KeyLoad__ServerExecution__EnableHttp2";
    private const string PortEnvironment = "KeyLoad__ServerExecution__Http2Port";
    private const string Enabled = "true";

    internal static void Apply(IResourceBuilder<ProjectResource> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        resource.WithHttpEndpoint(name: Http2Endpoint, isProxied: false)
            .WithEnvironment(EnableEnvironment, Enabled)
            .WithEnvironment(PortEnvironment, resource.GetEndpoint(Http2Endpoint).Property(EndpointProperty.TargetPort));
    }
}
