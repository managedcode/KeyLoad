using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

/// <summary>Owns one real development silo or the same real three-silo functional scenario through Aspire.</summary>
internal sealed partial class FunctionalGateFixture : IAsyncDisposable
{
    private const int SingleSilo = 1;
    private const int ThreeSilos = 3;
    private readonly Lock disposalGate = new();
    private readonly bool coldReopen;
    private IDistributedApplicationTestingBuilder? builder;
    private Task? disposal;
    private ClusterFixtureDiagnostics? diagnostics;
    private string[] diagnosticSecrets = [];
    private Action<string>? saveStartupEvidence;
    private bool started;
    private string? processEvidenceDirectory;
    internal ProcessRuntimeControl? ColdRuntime
    {
        get; private set;
    }
    internal DistributedApplication App { get; private set; } = null!;
    internal string AdminKey { get; private set; } = string.Empty;
    internal string[] NodeNames
    {
        get;
    }
    internal int ReplicaCount { get; }
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "keyload-functional-" + Guid.NewGuid().ToString("N"));
    internal bool PreserveEvidence
    {
        get; set;
    }

    private FunctionalGateFixture(int nodes, int replicas, bool coldReopen, bool exclusiveOwnerContention)
    {
        this.coldReopen = coldReopen;
        fileContenders = new(exclusiveOwnerContention);
        NodeNames = Enumerable.Range(SingleSilo, nodes).Select(ClusterFixtureProtocol.NodeName).ToArray();
        ReplicaCount = replicas;
    }

    internal static Task<FunctionalGateFixture> StartAsync(int nodes, int replicas, CancellationToken token)
        => StartCoreAsync(nodes, replicas, false, false, token);

    internal static Task<FunctionalGateFixture> StartColdAsync(int nodes, int replicas, CancellationToken token)
        => StartCoreAsync(nodes, replicas, true, false, token);

    private static async Task<FunctionalGateFixture> StartCoreAsync(int nodes, int replicas, bool coldReopen,
        bool exclusiveOwnerContention, CancellationToken token)
    {
        if (nodes is not (SingleSilo or ThreeSilos))
        {
            throw new ArgumentOutOfRangeException(nameof(nodes));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(replicas);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(replicas, nodes);
        token.ThrowIfCancellationRequested();
        var owner = new FunctionalGateFixture(nodes, replicas, coldReopen, exclusiveOwnerContention);
        return await FunctionalGateFixtureStartup.InitializeAsync(owner,
            () => owner.InitializeAsync(nodes, token)).ConfigureAwait(false);
    }

    private async Task InitializeAsync(int nodes, CancellationToken token)
    {
        var arguments = FunctionalGateFixtureStartup.Arguments(Root, nodes, ReplicaCount, coldReopen,
            fileContenders.Enabled);
        using var startupBound = new CancellationTokenSource(TimeSpan.FromMinutes(2), TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(token, startupBound.Token);
        builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(arguments.ToArray(), startup.Token)
            .ConfigureAwait(false);
        ClusterFixtureComposition.ConfigureLogging(builder);
        var resources = FunctionalGateProjectModel.ReadResourceNames(builder, NodeNames);
        App = await builder.BuildAsync(startup.Token).ConfigureAwait(false);
        var profile = ClusterFixtureProfile.ReadProfile(Root);
        AdminKey = profile.AdminKey;
        diagnosticSecrets = [profile.SigningKey, profile.AdminKey];
        saveStartupEvidence = App.Services.GetRequiredService<ClusterFixtureStartupEvidence>().Save;
        diagnostics = new(App);
        diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        ColdRuntime = new(App, resources, repository);
        await App.StartAsync(startup.Token).ConfigureAwait(false);
        await AspireStartupReadiness.WaitForHealthyAsync(App, NodeNames, startup.Token).ConfigureAwait(false);
        processEvidenceDirectory = new ClusterFailureReceipts().RequireRunOutput(TestContext.ResultsDirectory);
        Directory.CreateDirectory(processEvidenceDirectory);
        _ = await ClusterFixtureApplicationStartup.CaptureAsync(App, NodeNames, processEvidenceDirectory,
            "execution-process-startup.json", startup.Token).ConfigureAwait(false);
        started = true;
        PhysicalFiles = await FunctionalGatePhysicalFileAssertions.CaptureLiveAsync(this, startup.Token).ConfigureAwait(false);
    }
}
