using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedKeyLoadResources
{
    private const string Target = "KeyLoad";
    private const string DataDirectory = "keyload";
    private const string AdminParameter = "admin-key";
    private const string AdminEnvironment = "Benchmarks__AdminKey";
    private const string HttpEndpoint = "http";
    private const string InvalidTarget = "IsolatedKeyLoadSelectionInvalid";

    internal static void Add(IsolatedResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidTarget);
        }
        var image = RuntimeContainerImage.Read(context.Builder, RuntimeContainerImage.ServerConfiguration);
        var directory = context.DataDirectory(DataDirectory);
        var profile = ClusterProfileStore.Open(directory);
        var nodes = ClusterResources.Add(context.Builder, profile, directory, ephemeral: true,
            benchmarkNodeCount: context.Selection.NodeCount);
        var admin = context.Builder.CreateResourceBuilder(context.Builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == AdminParameter));
        context.Runner.WithEnvironment(AdminEnvironment, admin);
        context.BindImage(image.Reference);
        for (var index = 0; index < nodes.Length; index++)
        {
            context.BindEndpoint(index, nodes[index], HttpEndpoint);
        }
    }
}
