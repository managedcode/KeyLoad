using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.Comparisons;

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
        const int IndexInitialValue = 0;

        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidTarget);
        }
        var runtime = KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.Get(context.Builder);
        if (runtime.IsolatedAdmission.Value.DocumentWorkload)
        { throw new InvalidOperationException(InvalidTarget); }
        var image = RuntimeContainerImage.Read(context.Builder, RuntimeContainerImage.ServerConfiguration);
        var directory = context.DataDirectory(DataDirectory);
        var profile = ClusterProfileStore.Open(directory, runtime.Profile);
        var nodes = ClusterResources.Add(context.Builder, profile, directory, ephemeral: true,
            benchmarkNodeCount: context.Selection.NodeCount);
        var admin = context.Builder.CreateResourceBuilder(context.Builder.Resources.OfType<ParameterResource>()
            .Single(parameter => parameter.Name == AdminParameter));
        context.Runner.WithEnvironment(AdminEnvironment, admin);
        context.BindImage(image.Reference);
        var selectedAdmission = context.Selection.DocumentWorkload is null ? runtime.IsolatedAdmission
            : KeyLoad.AppHost.Hosting.AppHostOptionsRegistration.BindDocumentAdmission(runtime.IsolatedAdmission, runtime.Deployment);
        var admission = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(selectedAdmission);
        IsolatedKeyLoadAdmission.ForwardSelection(context.Runner, selectedAdmission);
        for (var index = IndexInitialValue; index < nodes.Length; index++)
        {
            IsolatedKeyLoadAdmission.Apply(nodes[index], admission, runtime.IsolatedReplayAdmission);
            if (context.Selection.DocumentWorkload is not null)
            { DocumentResourceBounds.ApplyKeyLoad(nodes[index], context.Runner, runtime.Deployment.Value); }
            context.BindEndpoint(index, nodes[index], HttpEndpoint);
        }
    }
}
