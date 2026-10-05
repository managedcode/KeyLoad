namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedHelixDbResources
{
    private const string Target = "HelixDB";
    private const string Name = "isolated-helixdb";
    private const string Image = "ghcr.io/helixdb/helixdb";
    private const string ImageTag = "v0.0.10";
    private const string ImageReference = "ghcr.io/helixdb/helixdb:v0.0.10@";
    private const string DataMount = "/var/lib/helix";
    private const string DataDirectorySetting = "HELIX_DATA_DIR";
    private const string Endpoint = "http";
    private const int Port = 8080;
    private const string Invalid = "IsolatedHelixDbSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target || context.Selection.NodeCount != 1)
        {
            throw new InvalidOperationException(Invalid);
        }

        var directory = context.DataDirectory(Name);
        ClusterProfileStore.PrepareDirectory(directory);
        var node = context.Builder.AddContainer(Name, Image, ImageTag)
            .WithImageSHA256(BenchmarkResources.HelixDbDigest[7..])
            .WithContainerNetworkAlias(Name)
            .WithBindMount(directory, DataMount)
            .WithEnvironment(DataDirectorySetting, DataMount)
            .WithHttpEndpoint(targetPort: Port, name: Endpoint)
            .WithHttpHealthCheck("/readyz");
        if (ClusterContainerUser.Resolve(context.Builder) is { } user)
        {
            node.WithContainerRuntimeArgs("--user", user);
        }

        context.BindEndpoint(0, node, Endpoint);
        context.BindImage(ImageReference + BenchmarkResources.HelixDbDigest);
    }
}
