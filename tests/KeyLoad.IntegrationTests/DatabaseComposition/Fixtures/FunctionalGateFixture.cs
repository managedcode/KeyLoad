using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
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
    private readonly FunctionalGateApplicationFixture aspire;
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
    internal ClusterIdentity Identity => App.Services.GetRequiredService<ClusterIdentity>();

    internal string[] NodeNames
    {
        get;
    }
    internal int ReplicaCount { get; }
    internal string Root { get; } = ClusterDataDirectory.Resolve(Path.Combine(AppContext.BaseDirectory, "keyload-functional-" + Guid.NewGuid().ToString("N")));
    internal bool PreserveEvidence
    {
        get; set;
    }

    private FunctionalGateFixture(int nodes, int replicas, bool coldReopen, bool exclusiveOwnerContention)
    {
        this.coldReopen = coldReopen;
        aspire = new(this);
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
            () => owner.InitializeOwnedAsync(token)).ConfigureAwait(false);
    }

    private async Task InitializeOwnedAsync(CancellationToken token)
    {
        ExecutionToken = token;
        await aspire.InitializeAsync().ConfigureAwait(false);
    }

    internal CancellationToken ExecutionToken { get; private set; }

    internal string[] CreateAspireArguments()
        => FunctionalGateFixtureStartup.Arguments(Root, NodeNames.Length, ReplicaCount, coldReopen,
            fileContenders.Enabled).ToArray();

    internal async Task ObserveReadyAsync(CancellationToken cancellationToken)
    {
        await AspireStartupReadiness.WaitForHealthyAsync(App, NodeNames, cancellationToken).ConfigureAwait(false);
        processEvidenceDirectory = new ClusterFailureReceipts().RequireRunOutput(TestContext.ResultsDirectory);
        Directory.CreateDirectory(processEvidenceDirectory);
        _ = await ClusterFixtureApplicationStartup.CaptureAsync(App, NodeNames, processEvidenceDirectory,
            "execution-process-startup.json", cancellationToken).ConfigureAwait(false);
        started = true;
        PhysicalFiles = await FunctionalGatePhysicalFileAssertions.CaptureLiveAsync(this, cancellationToken).ConfigureAwait(false);
    }

    internal void OwnApplication(DistributedApplication app)
    {
        App = app;
        var profile = Identity;
        AdminKey = profile.AdminKey;
        diagnosticSecrets = [profile.SigningKey, profile.AdminKey];
        saveStartupEvidence = App.Services.GetRequiredService<ClusterFixtureStartupEvidence>().Save;
        diagnostics = new(App);
        diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
        ColdRuntime = new(App, NodeNames, ClusterFixtureDiagnostics.FindRepositoryRoot().FullName);
    }
}
