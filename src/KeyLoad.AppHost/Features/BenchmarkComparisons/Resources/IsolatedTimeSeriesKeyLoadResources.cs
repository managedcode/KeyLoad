using System.Globalization;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedTimeSeriesKeyLoadResources
{
    private const int FirstEndpointIndex = 0;
    private const string DataDirectory = "keyload";
    private const string AdminParameter = "admin-key";
    private const string IncarnationParameter = "incarnation";
    private const string NativeIncarnationSetting = "Incarnation";
    private const string NativeVoterIdsPrefix = "VoterIds__";
    private const string AdminEnvironment = "Benchmarks__AdminKey";
    private const string HttpEndpoint = "http";

    internal static void Add(IsolatedTimeSeriesResourceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var image = RuntimeContainerImage.Read(context.Builder, RuntimeContainerImage.ServerConfiguration);
        var directory = context.DataDirectory(DataDirectory);
        var profile = ClusterProfileStore.Open(directory, KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(context.Builder).Profile);
        var nodes = ClusterResources.Add(context.Builder, profile, directory, ephemeral: true,
            benchmarkNodeCount: context.NodeCount);
        var admin = context.Builder.CreateResourceBuilder(context.Builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == AdminParameter));
        var incarnation = context.Builder.CreateResourceBuilder(context.Builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == IncarnationParameter));
        context.Runner.WithEnvironment(AdminEnvironment, admin);
        context.BindSetting(NativeIncarnationSetting, incarnation);
        context.BindImage(image.Reference);
        var runtime = KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(context.Builder);
        var admission = KeyLoad.Comparisons.IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(runtime.IsolatedAdmission);
        IsolatedKeyLoadAdmission.ForwardSelection(context.Runner, runtime.IsolatedAdmission);
        for (var index = FirstEndpointIndex; index < nodes.Length; index++)
        {
            IsolatedKeyLoadAdmission.Apply(nodes[index], admission, runtime.IsolatedReplayAdmission);
            context.BindSetting(NativeVoterIdsPrefix + index.ToString(CultureInfo.InvariantCulture),
                ClusterResources.Origin(nodes[index].Resource.Name));
            context.BindEndpoint(index, nodes[index], HttpEndpoint);
        }
    }
}
