using KeyLoad.AppHost.Hosting;

internal static class ClusterResourceSettings
{
    private const string LogEnvironment = "Logging__LogLevel__Default";

    internal static void Apply(IDistributedApplicationBuilder builder, IResourceBuilder<ProjectResource> resource)
    {
        KeyLoad.AppHost.OrleansHosting.ClusterTelemetryResources.Apply(resource);
        var options = AppHostOptionsRegistration.Get(builder);
        resource.WithEnvironment(LogEnvironment, options.Cluster.Value.LoggingLevel);
    }
}
