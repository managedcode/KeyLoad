using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTimeSeriesKeyLoadResources
{
    private const int FirstEndpointIndex = 0;
    private const string DataDirectory = "keyload";
    private const string AdminParameter = "admin-key";
    private const string AdminEnvironment = "Benchmarks__AdminKey";
    private const string HttpEndpoint = "http";

    internal static void Add(IsolatedTimeSeriesResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var image = RuntimeContainerImage.Read(context.Builder, RuntimeContainerImage.ServerConfiguration);
        var directory = context.DataDirectory(DataDirectory);
        var profile = ClusterProfileStore.Open(directory);
        var nodes = ClusterResources.Add(context.Builder, profile, directory, ephemeral: true,
            benchmarkNodeCount: context.NodeCount);
        var admin = context.Builder.CreateResourceBuilder(context.Builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == AdminParameter));
        context.Runner.WithEnvironment(AdminEnvironment, admin);
        context.BindImage(image.Reference);
        for (var index = FirstEndpointIndex; index < nodes.Length; index++)
        {
            IsolatedKeyLoadAdmission.Apply(nodes[index]);
            context.BindEndpoint(index, nodes[index], HttpEndpoint);
        }
    }
}
