using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3Wave : IAsyncDisposable
{
    private const string DueLeaderLossScenario = "messaging-due-leader-loss";
    private const string DueNoQuorumScenario = "messaging-due-no-quorum";
    private readonly string dataRoot;
    private readonly ContainerRuntimeControl runtime;
    private readonly RequestCqrsRf3Diagnostics diagnostics;
    private DistributedApplication? application;

    private RequestCqrsRf3Wave(string dataRoot, ref DistributedApplication? application,
        ContainerRuntimeControl runtime, ref RequestCqrsRf3Diagnostics? diagnostics)
    {
        if (application is null || diagnostics is null)
        {
            throw new InvalidOperationException("The C1 wave transfer requires its owned Aspire resources.");
        }
        this.dataRoot = dataRoot;
        this.application = application;
        this.runtime = runtime;
        this.diagnostics = diagnostics;
        application = null;
        diagnostics = null;
    }

    internal DistributedApplication App => application ?? throw new ObjectDisposedException(nameof(RequestCqrsRf3Wave));

    internal static Task<RequestCqrsRf3Wave> StartAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        CancellationToken cancellationToken)
        => StartAsync(dataRoot, images, configureCohort, requireHealthy, Guid.NewGuid(), cancellationToken);

    internal static Task<RequestCqrsRf3Wave> StartAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        Guid diagnosticsWaveId, CancellationToken cancellationToken)
        => RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort, requireHealthy,
            null, diagnosticsWaveId, cancellationToken);

    internal static Task<RequestCqrsRf3Wave> StartPriorNative6Async(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        CancellationToken cancellationToken)
        => StartPriorNative6Async(dataRoot, images, configureCohort, requireHealthy,
            Guid.NewGuid(), cancellationToken);

    internal static Task<RequestCqrsRf3Wave> StartProbedAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, RequestCqrsProbeFixture controls,
        CancellationToken cancellationToken)
        => RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort: false, requireHealthy: true,
            null, Guid.NewGuid(), cancellationToken, controls);

    internal static Task<RequestCqrsRf3Wave> StartPriorNative6Async(string dataRoot,
        IReadOnlyDictionary<string, string> images, bool configureCohort, bool requireHealthy,
        Guid diagnosticsWaveId, CancellationToken cancellationToken)
        => RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort, requireHealthy,
            NodeEpochRf3Protocol.SnapshotThresholdArgument, diagnosticsWaveId, cancellationToken);

    internal static RequestCqrsRf3Wave TransferOwned(string dataRoot, ContainerRuntimeControl runtime,
        ref DistributedApplication? application, ref RequestCqrsRf3Diagnostics? diagnostics)
        => new(dataRoot, ref application, runtime, ref diagnostics);

    internal Task KillAsync(string node, CancellationToken cancellationToken)
        => runtime.KillAsync(node, RequestCqrsRf3Protocol.FollowerLossScenario, cancellationToken);

    internal Task KillLeaderAsync(string node, CancellationToken cancellationToken)
    {
        if (!IsNode(node))
        { throw new ArgumentOutOfRangeException(nameof(node)); }
        return runtime.KillAsync(node, DueLeaderLossScenario, cancellationToken);
    }

    internal Task KillDueNoQuorumAsync(string node, CancellationToken cancellationToken)
    {
        if (!IsNode(node))
        { throw new ArgumentOutOfRangeException(nameof(node)); }
        return runtime.KillAsync(node, DueNoQuorumScenario, cancellationToken);
    }

    internal void SaveFailureEvidence(Exception failure) => diagnostics.SaveFailureEvidence(failure);

    internal string SaveDiagnosticsEvidence() => diagnostics.SaveEvidence();

    internal Task WaitForRejectionAsync(string node, McpTransportStage stage,
        McpTransportMethodCategory methodCategory, CancellationToken cancellationToken)
        => diagnostics.WaitForRecordAsync(node, stage, methodCategory, cancellationToken);

    internal Task RestartAsync(string node, CancellationToken cancellationToken)
        => runtime.RestartAsync(node, cancellationToken);

    internal async Task StopAsync()
    {
        var owned = application;
        if (owned is null)
        { return; }
        var failures = new List<Exception>();
        await CompleteAsync(owned, diagnostics, failures).ConfigureAwait(false);
        if (failures.Count == 0)
        {
            ServerFailureObserver.Observe(() => AssertNodeLocksReleased(dataRoot), failures);
            application = null;
        }
        SaveFailureEvidence(diagnostics, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    internal static async Task CompleteAsync(DistributedApplication app,
        RequestCqrsRf3Diagnostics? diagnostics, List<Exception> failures)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline);
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        if (diagnostics is not null)
        {
            await ServerFailureObserver.ObserveAsync(
                () => diagnostics.CompleteAndDrainAsync(deadline.Token), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        SaveFailureEvidence(diagnostics, failures);
    }

    internal static void SaveFailureEvidence(RequestCqrsRf3Diagnostics? diagnostics, List<Exception> failures)
    {
        if (diagnostics is not null && failures.Count > 0)
        { ServerFailureObserver.Observe(() => diagnostics.SaveFailureEvidence(failures[0]), failures); }
    }

    private static void AssertNodeLocksReleased(string root)
    {
        foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
        {
            var directory = Path.Combine(root, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(directory, "replica", "owner.lock"));
        }
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
