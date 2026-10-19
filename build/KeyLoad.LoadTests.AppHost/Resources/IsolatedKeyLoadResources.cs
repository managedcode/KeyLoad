using KeyLoad.Comparisons;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

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
        const int FirstNodeIndex = 0;
        ArgumentNullException.ThrowIfNull(context);
        context.Selection.Validate();
        if (context.Selection.Target != Target)
        {
            throw new InvalidOperationException(InvalidTarget);
        }
        var runtime = LoadTestsOptionsRegistration.Get(context.Builder);
        if (runtime.IsolatedAdmission.Value.DocumentWorkload)
        {
            throw new InvalidOperationException(InvalidTarget);
        }
        var directory = context.DataDirectory(DataDirectory);
        var profile = ClusterProfileStore.Open(directory, runtime.Execution.Profile);
        var nodes = KeyLoad.AppHost.Hosting.AppHostProgram.AddCluster(context.Builder, profile, directory, ephemeral: true,
            benchmarkNodeCount: context.Selection.NodeCount);
        var admin = context.Builder.Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == AdminParameter);
        context.Client.Set(AdminEnvironment, admin);
        var selectedAdmission = LoadTestsOptionsRegistration.BindDocumentAdmission(runtime.IsolatedAdmission, runtime.Deployment);
        var admission = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(selectedAdmission);
        IsolatedKeyLoadAdmission.ForwardSelection(context.Client, selectedAdmission);
        for (var index = FirstNodeIndex; index < nodes.Length; index++)
        {
            IsolatedKeyLoadAdmission.Apply(nodes[index], admission);
            DocumentResourceBounds.ApplyKeyLoad(nodes[index], runtime.Deployment.Value);
            context.BindEndpoint(index, nodes[index], HttpEndpoint);
        }
        DocumentResourceBounds.ApplyClient(context.Client, runtime.Deployment.Value);
    }
}
