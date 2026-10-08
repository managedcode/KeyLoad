using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Query;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsRf3Wave : IAsyncDisposable
{
    private const string DueLeaderLossScenario = "messaging-due-leader-loss";
    private const string DueNoQuorumScenario = "messaging-due-no-quorum";
    private readonly string dataRoot;
    private readonly ContainerRuntimeControl runtime;
    private readonly RequestCqrsRf3Diagnostics diagnostics;
    private readonly RequestCqrsLifecycleEvidence? lifecycleEvidence;
    private DistributedApplication? application;
    private IDistributedApplicationTestingBuilder? testingBuilder;
    private Action<RequestCqrsLifecycleStage>? FailureObserver => lifecycleEvidence is null
        ? null : lifecycleEvidence.RecordOwnerFailure;

    private RequestCqrsRf3Wave(string dataRoot, ref DistributedApplication? application,
        ContainerRuntimeControl runtime, ref RequestCqrsRf3Diagnostics? diagnostics, ref IDistributedApplicationTestingBuilder? testingBuilder,
        RequestCqrsLifecycleEvidence? lifecycleEvidence)
    {
        if (application is null || diagnostics is null || testingBuilder is null)
        {
            throw new InvalidOperationException("The C1 wave transfer requires its owned Aspire resources.");
        }
        this.dataRoot = dataRoot;
        this.application = application;
        this.runtime = runtime;
        this.testingBuilder = testingBuilder;
        testingBuilder = null;
        this.diagnostics = diagnostics;
        this.lifecycleEvidence = lifecycleEvidence;
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
        Guid diagnosticsWaveId, CancellationToken cancellationToken,
        string? physicalShardOverrideNode = null, Guid? physicalShardOverrideId = null,
        RequestCqrsProbeFixture? controls = null)
        => RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort, requireHealthy,
            null, diagnosticsWaveId, cancellationToken, controls, physicalShardOverrideNode: physicalShardOverrideNode,
            physicalShardOverrideId: physicalShardOverrideId);

    internal static Task<RequestCqrsRf3Wave> StartProbedAsync(string dataRoot,
        IReadOnlyDictionary<string, string> images, RequestCqrsProbeFixture controls,
        CancellationToken cancellationToken, RequestCqrsLifecycleEvidence? lifecycle = null,
        QueryExecutionOptions? queryExecution = null)
        => RequestCqrsRf3WaveStartup.StartAsync(dataRoot, images, configureCohort: false, requireHealthy: true,
            null, Guid.NewGuid(), cancellationToken, controls, lifecycleEvidence: lifecycle, queryExecution: queryExecution);

    internal static RequestCqrsRf3Wave TransferOwned(string dataRoot, ContainerRuntimeControl runtime,
        ref DistributedApplication? application, ref RequestCqrsRf3Diagnostics? diagnostics,
        ref IDistributedApplicationTestingBuilder? testingBuilder, RequestCqrsLifecycleEvidence? lifecycleEvidence = null)
        => new(dataRoot, ref application, runtime, ref diagnostics, ref testingBuilder, lifecycleEvidence);

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
        await CompleteAsync(owned, diagnostics, failures, lifecycleEvidence).ConfigureAwait(false);
        if (await RequestCqrsRf3BuilderCleanup.DisposeAsync(testingBuilder, failures, FailureObserver).ConfigureAwait(false))
        { testingBuilder = null; }
        if (failures.Count == 0)
        {
            RequestCqrsLifecycleFailureObserver.Observe(() => AssertNodeLocksReleased(dataRoot), failures,
                FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveLockCheck);
            application = null;
        }
        if (failures.Count > 0)
        {
            RequestCqrsLifecycleFailureObserver.Observe(
                () => diagnostics.SaveFailureEvidence(failures[0]), failures,
                FailureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsArtifact);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    internal static async Task CompleteAsync(DistributedApplication app,
        RequestCqrsRf3Diagnostics? diagnostics, List<Exception> failures,
        RequestCqrsLifecycleEvidence? lifecycleEvidence = null)
    {
        Action<RequestCqrsLifecycleStage>? failureObserver = lifecycleEvidence is null
            ? null : lifecycleEvidence.RecordOwnerFailure;
        async Task CompleteOwnedAsync()
        {
            using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System);
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures,
                failureObserver, RequestCqrsLifecycleStage.AuthorityWaveAppStop).ConfigureAwait(false);
            if (diagnostics is not null)
            {
                await RequestCqrsLifecycleFailureObserver.ObserveAsync(
                    () => diagnostics.CompleteAndDrainAsync(deadline.Token), failures,
                    failureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsDrain).ConfigureAwait(false);
            }
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures,
                failureObserver, RequestCqrsLifecycleStage.AuthorityWaveAppDispose).ConfigureAwait(false);
        }
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(CompleteOwnedAsync, failures, failureObserver,
            RequestCqrsLifecycleStage.AuthorityCleanupDeadlineDispose).ConfigureAwait(false);
        var ownedDiagnostics = diagnostics;
        if (ownedDiagnostics is not null && failures.Count > 0)
        {
            RequestCqrsLifecycleFailureObserver.Observe(
                () => ownedDiagnostics.SaveFailureEvidence(failures[0]), failures,
                failureObserver, RequestCqrsLifecycleStage.AuthorityWaveDiagnosticsArtifact);
        }
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
