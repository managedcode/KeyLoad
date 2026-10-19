using System.Globalization;
using KeyLoad.Comparisons;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

/// <summary>Composes selected database infrastructure and exposes execution endpoints to a separately owned workload client.</summary>
internal static class BenchmarkSessionResources
{
    private const string ClientSession = "Benchmarks__Session";
    private const string ClientIdentity = "Benchmarks__SessionId";
    private const string ClientTarget = "Benchmarks__Target";
    private const string ClientNodes = "Benchmarks__NodeCount";
    private const string ClientProfile = "Benchmarks__EvidenceProfile";
    private const string ClientStorage = "Benchmarks__Storage";
    private const string ClientOutput = "Benchmarks__Output";
    private const string ClientSource = "Benchmarks__SourceRevision";
    private const string Enabled = "true";
    private const string Storage = "One fresh Aspire-owned database for the selected client workload lifecycle";

    internal static void Add(IDistributedApplicationBuilder builder, BenchmarkSessionResourceOptions session)
    {
        var runtime = LoadTestsOptionsRegistration.Get(builder);
        if (!runtime.Startup.Value.BenchmarkMode || runtime.Startup.Value.BenchmarkRoot is not { } root
            || runtime.Startup.Value.BenchmarkOutput is not { } output
            || session.SessionId is not { } sessionId)
        {
            throw new InvalidOperationException(BenchmarkSessionResourceOptions.Profile);
        }
        var selection = BenchmarkSessionContract.ResourceSelection(session.Target!, session.NodeCount);
        var client = new LoadTestClientSettings();
        client.Set(ClientSession, Enabled);
        client.Set(ClientIdentity, sessionId);
        client.Set(ClientTarget, session.Target!);
        client.Set(ClientNodes, session.NodeCount.ToString(CultureInfo.InvariantCulture));
        client.Set(ClientProfile, BenchmarkSessionResourceOptions.Profile);
        client.Set(ClientStorage, Storage);
        client.Set(ClientOutput, Path.GetFullPath(output));
        if (string.Equals(runtime.Provenance.Value.Actions, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            client.Set(ClientSource, runtime.Provenance.Value.SourceRevision
                ?? throw new InvalidOperationException(BenchmarkSessionResourceOptions.Profile));
        }
        builder.Services.AddSingleton(client);
        var context = new IsolatedResourceContext(builder, selection, client, Path.GetFullPath(root));
        builder.Services.AddSingleton(serviceProvider => new ScaleServerResourceEvidenceCollector(selection, output,
            applicationStopping: serviceProvider.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping,
            executionOptions: runtime.ServerResources, provenanceOptions: runtime.Provenance,
            provider: serviceProvider.GetRequiredService<TimeProvider>(), sessionId: sessionId));
        if (BenchmarkSessionContract.UnsupportedTopologies.Any(item => item.Target == session.Target
            && item.NodeCounts.Contains(session.NodeCount)))
        {
            return;
        }
        IsolatedDispatcher.Add(context);
        LoadTestResourceLifetime.Apply(builder);
    }
}
