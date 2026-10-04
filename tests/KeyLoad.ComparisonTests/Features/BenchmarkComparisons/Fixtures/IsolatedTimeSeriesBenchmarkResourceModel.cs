using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedTimeSeriesBenchmarkResourceModel : IAsyncDisposable
{
    private const string TemporaryRootPrefix = "keyload-timeseries-input-model-";
    private const string MissingCheckout = "The unstarted resource model requires the source checkout.";
    private const string SolutionFileName = "KeyLoad.slnx";
    private const string ProjectPath = "src/KeyLoad.AppHost";
    private const string RunnerImageKey = "Benchmarks:ContainerImages:LoadGenerator";
    private const string ServerImageKey = "KeyLoad:ContainerImages:Server";
    private const string KeyLoadContainerUserKey = "KeyLoad:ContainerUser";
    private const string DefaultOutputName = "reports";
    private const string RouteSetting = "Benchmarks:Profile";
    private const string RouteValue = "timeseries-intensive";
    private const string TargetSetting = "Benchmarks:TimeSeries:Target";
    private const string NodeCountSetting = "Benchmarks:TimeSeries:NodeCount";
    private const string PhaseSetting = "Benchmarks:TimeSeries:Phase";
    private const string ScenarioSetting = "Benchmarks:TimeSeries:Scenario";
    private const string EvidenceProfileSetting = "Benchmarks:TimeSeries:EvidenceProfile";
    private const string CellIdSetting = "Benchmarks:TimeSeries:CellId";
    private const string ContractHashSetting = "Benchmarks:TimeSeries:ContractSha256";
    private const string GuidFormat = "N";
    private const string ContainerUser = "1001:1001";
    private DistributedApplication? application;
    internal IsolatedTimeSeriesBenchmarkResourceModel(bool configurePaths = true)
    {
        var root = Path.Combine(Path.GetTempPath(), TemporaryRootPrefix + Guid.NewGuid().ToString(GuidFormat));
        Root = root;
        Output = Path.Combine(root, DefaultOutputName);
        Builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            DisableDashboard = true,
            ProjectDirectory = Path.Combine(RepositoryRoot(), ProjectPath),
            Args = [$"--{ServerImageKey}={ServerImage}", $"--{RunnerImageKey}={RunnerImage}",
                $"--{KeyLoadContainerUserKey}={ContainerUser}"]
        });
        if (configurePaths)
        {
            Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [RootKey] = Root,
                [OutputKey] = Output
            });
        }
    }
    private const string RootKey = "Benchmarks:DataRoot";
    private const string OutputKey = "Benchmarks:Output";
    private const string ServerImage = IsolatedResourceTopologyFixture.ServerImage;
    private const string RunnerImage = "ghcr.io/managedcode/keyload-comparison-runner:model@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal IDistributedApplicationBuilder Builder { get; }
    internal string Root { get; private set; }
    internal string Output { get; }
    internal void SetRootForCleanup(string root) => Root = root;
    internal void ConfigureSelection(TimeSeriesIntensiveFamilyCell cell, bool includeIdentity = true)
    {
        var selection = cell.Selection;
        var settings = new Dictionary<string, string?>
        {
            [RouteSetting] = RouteValue,
            [TargetSetting] = selection.Target.ToString(),
            [NodeCountSetting] = selection.NodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [PhaseSetting] = selection.Phase.ToString(),
            [EvidenceProfileSetting] = selection.EvidenceProfile
        };
        if (includeIdentity)
        {
            settings[CellIdSetting] = cell.Id;
            settings[ContractHashSetting] = TimeSeriesIntensiveFamilyContract.Current.ContractSha256;
        }
        if (selection.Scenario is { } scenario)
        {
            settings[ScenarioSetting] = scenario.ToString();
        }
        Builder.Configuration.AddInMemoryCollection(settings);
    }
    internal ContainerResource[] Build()
    {
        application = Builder.Build();
        return application.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<ContainerResource>().ToArray();
    }
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync();
        }
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
    private static string RepositoryRoot()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, SolutionFileName)))
            {
                return directory;
            }
        }
        throw new InvalidOperationException(MissingCheckout);
    }
}
