using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class IsolatedBenchmarkResources
{
    private const string DirectoryIdentityFormat = "N";

    private const string EnabledSetting = "Benchmarks:Enabled";
    private const string OutputSetting = "Benchmarks:Output";
    private const string RootSetting = "Benchmarks:DataRoot";
    private const string TemporaryPrefix = "keyload-isolated-";
    private const string ReportsDirectory = "reports";
    private const string SettingSeparator = ":";
    private const string EnvironmentSeparator = "__";
    private const string StorageSetting = "Benchmarks__Storage";
    private const string Storage = "Fresh cell-owned native directories and volumes; no shared database";
    private const string Disabled = "Isolated comparison selection requires explicit benchmark mode.";

    internal static void Add(IDistributedApplicationBuilder builder)
    {
        var selection = ComparisonWorkerSelection.Read(builder.Configuration);
        if (!bool.TryParse(builder.Configuration[EnabledSetting], out var enabled) || !enabled)
        {
            throw new InvalidOperationException(Disabled);
        }
        var root = Path.GetFullPath(builder.Configuration[RootSetting]
            ?? Path.Combine(Path.GetTempPath(), TemporaryPrefix + Guid.NewGuid().ToString(DirectoryIdentityFormat)));
        var runner = BenchmarkRunnerContainer.Create(builder,
            builder.Configuration[OutputSetting] ?? Path.Combine(root, ReportsDirectory));
        Bind(runner, ComparisonWorkerSelection.TargetSetting, selection.Target);
        Bind(runner, ComparisonWorkerSelection.NodeCountSetting, selection.NodeCount.ToString(CultureInfo.InvariantCulture));
        Bind(runner, ComparisonWorkerSelection.ScenarioSetting, selection.Scenario.ToString());
        Bind(runner, ComparisonWorkerSelection.ProfileSetting, selection.Profile);
        if (selection.VectorProfile is { } vectorProfile)
        {
            Bind(runner, ComparisonWorkerSelection.VectorProfileSetting, vectorProfile.Id);
        }
        runner.WithEnvironment(StorageSetting, Storage);
        var context = new IsolatedResourceContext(builder, selection, runner, root);
        if (selection.ScaledProfile is not null || selection.VectorProfile is not null)
        {
            builder.Services.AddSingleton(serviceProvider => new ScaleServerResourceEvidenceCollector(selection,
                builder.Configuration[OutputSetting] ?? Path.Combine(root, ReportsDirectory),
                serviceProvider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping));
        }
        if (IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
                item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount)))
        {
            context.BindImage(IsolatedUnsupportedTargetImage.For(selection.Target));
            return;
        }
        IsolatedNativeDispatcher.Add(context);
    }

    private static void Bind(IResourceBuilder<ContainerResource> runner, string name, string value)
        => runner.WithEnvironment(name.Replace(SettingSeparator, EnvironmentSeparator, StringComparison.Ordinal), value);
}
