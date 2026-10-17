using System.Globalization;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Composes one Aspire-owned native topology and a shared multi-test runner.</summary>
internal static class BenchmarkSessionResources
{
    private const string RunnerSession = "Benchmarks__Session";
    private const string RunnerIdentity = "Benchmarks__SessionId";
    private const string RunnerTarget = "Benchmarks__Target";
    private const string RunnerNodes = "Benchmarks__NodeCount";
    private const string RunnerProfile = "Benchmarks__EvidenceProfile";
    private const string RunnerStorage = "Benchmarks__Storage";
    private const string Enabled = "true";
    private const string Storage = "One Aspire-owned native database retained across every session test";

    internal static void Add(IDistributedApplicationBuilder builder, BenchmarkSessionResourceOptions session)
    {
        var runtime = AppHostOptionsRegistration.Get(builder);
        if (!runtime.Startup.Value.BenchmarkMode || runtime.Startup.Value.BenchmarkRoot is not { } root
            || runtime.Startup.Value.BenchmarkOutput is not { } output
            || session.SessionId is not { } sessionId)
        {
            throw new InvalidOperationException(BenchmarkSessionResourceOptions.Profile);
        }
        var selection = BenchmarkSessionContract.ResourceSelection(session.Target!, session.NodeCount);
        var runner = BenchmarkRunnerContainer.Create(builder, output)
            .WithEnvironment(RunnerSession, Enabled).WithEnvironment(RunnerIdentity, sessionId)
            .WithEnvironment(RunnerTarget, session.Target)
            .WithEnvironment(RunnerNodes, session.NodeCount.ToString(CultureInfo.InvariantCulture))
            .WithEnvironment(RunnerProfile, BenchmarkSessionResourceOptions.Profile)
            .WithEnvironment(RunnerStorage, Storage);
        var context = new IsolatedResourceContext(builder, selection, runner, Path.GetFullPath(root));
        builder.Services.AddSingleton(serviceProvider => new ScaleServerResourceEvidenceCollector(selection, output,
            applicationStopping: serviceProvider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
            executionOptions: runtime.ServerResources, provenanceOptions: runtime.Provenance,
            provider: serviceProvider.GetRequiredService<TimeProvider>(), sessionId: sessionId));
        if (BenchmarkSessionContract.UnsupportedTopologies.Any(item => item.Target == session.Target
            && item.NodeCounts.Contains(session.NodeCount)))
        {
            context.BindImage(IsolatedUnsupportedTargetImage.For(session.Target!));
            return;
        }
        IsolatedNativeDispatcher.Add(context);
    }
}
