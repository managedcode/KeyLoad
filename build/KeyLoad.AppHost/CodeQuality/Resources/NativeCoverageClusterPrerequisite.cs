using System.Globalization;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.AppHost.Features.CodeQuality;

internal static class NativeCoverageClusterPrerequisite
{
    internal const string ResourceName = "prepare-original-node-coverage-image";
    private const string PreparationScript = "scripts/Features/CodeQuality/functional-coverage.cluster-image-preparation.mjs";
    private const string MaterializerScript = "scripts/Features/CodeQuality/functional-coverage.server-image.mjs";
    private const string NodeExecutable = "node";
    private const string PrepareCommand = "prepare";

    internal static IResourceBuilder<ExecutableResource> Add(IDistributedApplicationBuilder builder,
        TestSuiteSettings settings, string repositoryRoot, string resultsDirectory, out NativeCoverageClusterRun run)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(resultsDirectory);
        var selection = settings.NativeCoverageCluster
            ?? throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        var options = AppHostOptionsRegistration.Get(builder).NativeCoverage;
        var execution = AppHostOptionsRegistration.Get(builder).TestExecution;
        run = NativeCoverageClusterRunManifestWriter.WriteAsync(resultsDirectory, selection.Admission,
            options, execution, CancellationToken.None).GetAwaiter().GetResult();
        var invocation = NativeCoverageClusterInvocationWriter.WriteAsync(repositoryRoot, run, options,
            CancellationToken.None).GetAwaiter().GetResult();
        var scriptPath = ResolveScript(repositoryRoot, PreparationScript);
        var materializerPath = ResolveScript(repositoryRoot, MaterializerScript);
        var seconds = checked((int)settings.Timeout.TotalSeconds);
        var args = new[]
        {
            scriptPath, PrepareCommand, invocation.Path, resultsDirectory, run.ImageReference, materializerPath,
            options.Value.MaximumDescriptorBytes.ToString(CultureInfo.InvariantCulture),
            seconds.ToString(CultureInfo.InvariantCulture),
            checked((int)options.Value.SettlementTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture)
        };
        var ownedRun = run;
        builder.Services.AddSingleton(services => new NativeCoverageClusterCleanup(ownedRun, invocation, options, execution, services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton(run);
        builder.Services.AddSingleton(invocation);
        return builder.AddExecutable(ResourceName, NodeExecutable, repositoryRoot, args);
    }

    private static string ResolveScript(string root, string relative)
    {
        const char Slash = '/';
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace(Slash, Path.DirectorySeparatorChar)));
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(NativeCoverageClusterProtocol.InvalidSelection);
        }
        return path;
    }
}
