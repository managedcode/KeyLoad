using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterFixtureResourceCleanup;

namespace KeyLoad.IntegrationTests;

/// <summary>Owns the real Aspire RF3 application shared by public SDK integration scenarios.</summary>
internal sealed class ClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private readonly RemoteTransferCoordinatorFixtureSelection? transferCoordinator;
    private readonly long? commandBytes;
    private readonly Features.Messaging.QueueDeadlineRf3Selection? queueDeadline;
    private readonly HttpAdmissionLimits? httpAdmission;
    private readonly DatabaseLimits? databaseLimits;
    private readonly int? snapshotThreshold;
    private readonly QueryExecutionOptions? queryExecution;
    private ClusterFixtureDiagnostics? diagnostics;
    private ContainerRuntimeControl? containerRuntime;
    private NativeCoverageRf3FixtureOwner? coverage;
    private LocalRf3ImageTestSession? localImageSession;
    private bool localImageCleanupIncomplete;
    private readonly bool isolateReplicaNamespace;
    private ReplicaIsolationOwner? isolationOwner;
    private ReplicaIsolationBuilderOwnership? isolationBuilder;
    private bool isolationCleanupIncomplete;
    private readonly string coverageFixtureId = Guid.NewGuid().ToString("D");
    private byte[] peerSecret = [];

    private const string RuntimeControllerUnavailable = "The Aspire container runtime controller is not initialized.";
    private const string DiagnosticsUnavailable = "The Aspire diagnostic capture is not initialized.";

    /// <summary>Creates the standard RF3 fixture for the shared TUnit class data source.</summary>
    public ClusterFixture() { }

    internal ClusterFixture(RemoteTransferCoordinatorFixtureSelection selection)
        => transferCoordinator = RemoteTransferCoordinatorFixtureConfiguration.Validate(selection);

    internal ClusterFixture(Features.Messaging.QueueDeadlineRf3Selection queueDeadline)
        => this.queueDeadline = queueDeadline;

    /// <summary>Creates only the explicitly selected Linux native namespace fault cohort.</summary>
    internal ClusterFixture(ReplicaIsolationProfile profile)
        => isolateReplicaNamespace = ReplicaIsolationAdmission.Require(profile);

    /// <summary>Creates an RF3 fixture with a smaller command admission budget.</summary>
    /// <param name="commandBytes">The per-node retained command byte limit.</param>
    internal ClusterFixture(long commandBytes) => this.commandBytes = commandBytes;

    /// <summary>Creates an RF3 fixture with explicit HTTP admission bounds.</summary>
    /// <param name="httpAdmission">The HTTP body and control-body limits for each node.</param>
    internal ClusterFixture(HttpAdmissionLimits httpAdmission)
    {
        ArgumentNullException.ThrowIfNull(httpAdmission);
        this.httpAdmission = httpAdmission;
    }

    /// <summary>Creates an RF3 fixture with the server's typed database execution limits.</summary>
    /// <param name="databaseLimits">The validated limits applied to every Aspire RF3 node.</param>
    internal ClusterFixture(DatabaseLimits databaseLimits)
        => this.databaseLimits = ClusterFixtureDatabaseLimits.Validate(databaseLimits);

    /// <summary>Creates an exclusive load fixture with an explicit positive snapshot cadence.</summary>
    internal ClusterFixture(DatabaseLimits databaseLimits, int snapshotThreshold) : this(databaseLimits)
        => this.snapshotThreshold = ClusterFixtureComposition.ValidateSnapshotThreshold(snapshotThreshold);

    /// <summary>Creates an RF3 fixture with explicit database and additional query result bounds.</summary>
    internal ClusterFixture(DatabaseLimits databaseLimits, QueryExecutionOptions queryExecution)
        : this(databaseLimits)
        => this.queryExecution = ClusterFixtureQueryResultLimits.Validate(queryExecution);

    /// <summary>Gets the unique private host directory used by the Aspire application.</summary>
    public string Root { get; private set; } = Path.Combine(Path.GetTempPath(),
        ClusterFixtureProtocol.RootDirectoryPrefix + Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat));

    /// <summary>Gets the actual started Aspire application and its allocated endpoint model.</summary>
    public DistributedApplication App { get; private set; } = null!;
    /// <summary>Gets the administrator credential loaded from the private local profile.</summary>
    public string AdminKey { get; private set; } = "";

    internal ClusterFixtureColdStartObservation? ColdStartObservation { get; private set; }

    /// <summary>Gets the opaque physical-shard identity from the actual private shared V2 profile.</summary>
    public Guid PhysicalShardId { get; private set; }

    /// <summary>Starts all three genuine Aspire resources and waits for their health concurrently.</summary>
    /// <returns>A task that completes when each of the three resources is healthy.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            using var preparation = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
            if (isolateReplicaNamespace)
            { await ReplicaIsolationAdmission.PreflightAsync(preparation.Token).ConfigureAwait(false); }
            localImageSession = await LocalRf3ImageTestSession.StartIfSelectedAsync(preparation.Token)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(StartupTimeout, TimeProvider.System);
            coverage = await NativeCoverageRf3FixtureOwner.CreateAsync(coverageFixtureId, timeout.Token)
                .ConfigureAwait(false);
            var arguments = coverage?.CreateArguments() ??
                [$"{ClusterFixtureProtocol.DataRootArgument}{Root}", ClusterFixtureProtocol.EphemeralArgument,
                    ClusterFixtureProtocol.SnapshotThresholdArgument];
            if (coverage is not null)
            {
                Root = coverage.Root;
            }
            if (localImageSession is not null)
            {
                arguments = [.. arguments, .. localImageSession.Selection.CreateWaveArguments()];
            }
            if (isolateReplicaNamespace && (localImageSession is not null || coverage is not null))
            { throw new InvalidOperationException("The native namespace fault cohort cannot mix local-image or covered selection."); }
            arguments = ClusterFixtureComposition.ApplySnapshotThreshold(arguments, snapshotThreshold,
                coverage is not null || localImageSession is not null || isolateReplicaNamespace);
            ColdStartObservation = ClusterFixtureApplicationStartup.ObserveColdStart(Root);
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
                arguments, timeout.Token);
            if (isolateReplicaNamespace)
            { isolationBuilder = new(builder); }
            ClusterFixtureComposition.ConfigureTestOverrides(builder, commandBytes, httpAdmission, databaseLimits, queryExecution);
            queueDeadline?.Configure(builder);
            RemoteTransferCoordinatorFixtureConfiguration.Configure(builder, transferCoordinator);
            var containerNames = ClusterFixtureComposition.GetContainerNames(builder);
            var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
            ClusterFixtureComposition.ConfigureLogging(builder);
            if (isolateReplicaNamespace)
            {
                isolationOwner = await ReplicaIsolationOwner.PrepareAsync(builder, Root, repository.FullName,
                    containerNames, timeout.Token).ConfigureAwait(false);
            }
            App = isolationBuilder is null ? await builder.BuildAsync(timeout.Token)
                : await isolationBuilder.BuildAsync(timeout.Token).ConfigureAwait(false);
            containerRuntime = new(App, containerNames, repository.FullName);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            await StartApplicationAsync(containerNames, repository.FullName, timeout.Token).ConfigureAwait(false);
            ReadPrivateProfile();
            if (isolationOwner is not null)
            { await isolationOwner.VerifyStartedAsync(App, timeout.Token).ConfigureAwait(false); }
        }
        catch (Exception startupFailure)
        {
            await ClusterFixtureStartupFailure.DisposeAndThrowAsync(this, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

    private Task StartApplicationAsync(IReadOnlyDictionary<string, string> containerNames,
        string repository, CancellationToken token)
        => isolationOwner is null
            ? ClusterFixtureApplicationStartup.StartAsync(App, repository, containerNames, coverage, localImageSession?.Selection, token)
            : isolationOwner.StartAsync(App, token);

    internal ReplicaIsolationOwner RequireReplicaIsolation() => isolationOwner
        ?? throw new InvalidOperationException("This fixture does not own a native namespace fault cohort.");

    internal void RegisterNativeCoverageCase<TCase>(string methodName) => coverage?.RegisterCase<TCase>(methodName);

    /// <summary>Creates the real .NET SDK client for one Aspire node and selected credential.</summary>
    /// <param name="node">The Aspire HTTP endpoint resource name.</param>
    /// <param name="key">An optional database credential; null selects the fixture administrator.</param>
    /// <returns>A client backed by Aspire's actual allocated HTTP endpoint.</returns>
    public KeyLoadClient Client(string node, string? key = null)
        => ClusterFixtureComposition.CreateClient(RequireApp(), node, key ?? AdminKey);

    /// <summary>Writes bounded RF3 state, signed discovery and recent node-log diagnostics.</summary>
    /// <returns>A task that completes after the diagnostic artifact is written.</returns>
    public Task SaveFailureDiagnosticsAsync() => SaveFailureDiagnosticsAsync(RequireDiagnostics().LifetimeToken);

    internal Task SaveReplicaIsolationFailureDiagnosticsAsync(string nativeCase)
        => RequireDiagnostics().SaveOwnedAsync(peerSecret, RequireReplicaIsolation().CreateFailureDirectory(nativeCase),
            RequireDiagnostics().LifetimeToken);

    /// <summary>Kills the inspected Aspire-managed Docker container for a named test scenario.</summary>
    /// <param name="resourceName">The Aspire resource name to stop.</param>
    /// <param name="scenario">The stable scenario identity recorded in the kill receipt.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed real Docker kill operation.</returns>
    public Task KillContainerAsync(string resourceName, string scenario, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenario);
        return RequireContainerRuntime().KillAsync(resourceName, scenario, cancellationToken);
    }

    /// <summary>Restarts a previously killed Aspire-managed container and records its actual new generation.</summary>
    /// <param name="resourceName">The Aspire resource name to restart.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed native Aspire resource start operation.</returns>
    public Task RestartContainerAsync(string resourceName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        return RequireContainerRuntime().RestartAsync(resourceName, cancellationToken);
    }

    /// <summary>Saves diagnostics, drains observers and disposes the actual Aspire application.</summary>
    /// <returns>A task that completes after the owned test resources are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        using var deadline = coverage?.CreateCleanupDeadline();
        using var localCleanup = localImageSession?.CreateApplicationCleanupCancellation();
        using var faultCleanup = isolateReplicaNamespace
            ? new CancellationTokenSource(RequestCqrsRf3Protocol.CleanupDeadline, TimeProvider.System) : null;
        var token = deadline?.Token ?? localCleanup?.Token ?? faultCleanup?.Token ?? CancellationToken.None;
        var failures = new List<Exception>();
        if (isolationOwner is not null)
        { await CollectCleanupAsync(deadline, () => isolationOwner.RestoreAsync(token), failures).ConfigureAwait(false); }
        await SaveDiagnosticsAsync(diagnostics, peerSecret, deadline, failures).ConfigureAwait(false);
        if (diagnostics is not null)
        {
            var disposal = diagnostics.DisposeAsync().AsTask();
            await CollectCleanupAsync(deadline, disposal, failures).ConfigureAwait(false);
            diagnostics = null;
        }
        var ownedApp = App;
        App = null!;
        var (appStopped, stoppedNodes) = await ClusterFixtureApplicationShutdown.CollectAsync(
            ownedApp, deadline, coverage, containerRuntime is not null, failures, token).ConfigureAwait(false);
        if (isolationBuilder is not null)
        {
            var disposal = isolationBuilder.DisposeAsync().AsTask();
            await CollectCleanupAsync(deadline, disposal, failures).ConfigureAwait(false);
        }
        if (isolationOwner is not null)
        {
            await CollectCleanupAsync(deadline, () => isolationOwner.SettleAfterStopAsync(appStopped, token), failures)
                .ConfigureAwait(false);
        }
        isolationCleanupIncomplete |= isolateReplicaNamespace && failures.Count > 0;
        var ownedLocalImage = localImageSession is not null;
        if (localImageSession is not null)
        {
            await ClusterFixtureLocalImageLifecycle.JoinAsync(localImageSession, Root, appStopped, ownedApp is null,
                failures).ConfigureAwait(false);
            localImageSession = null;
        }
        localImageCleanupIncomplete |= ownedLocalImage && failures.Count > 0;
        if (!localImageCleanupIncomplete && !isolationCleanupIncomplete)
        { await DeleteOwnedRootAsync(coverage, Root, deadline, failures).ConfigureAwait(false); }
        if (coverage is not null && appStopped && failures.Count == 0 && stoppedNodes is not null)
        {
            await deadline!.CollectAsync(() => coverage.PublishReceiptAsync(stoppedNodes, token), failures)
                .ConfigureAwait(false);
        }
        if (failures.Count == 1 && coverage is not null)
        { Rethrow(failures[0]); }
        if (failures.Count > 0)
        {
            throw new AggregateException("Cluster fixture cleanup failed.", failures);
        }
    }

    private void ReadPrivateProfile()
    {
        (AdminKey, peerSecret, PhysicalShardId) = ClusterFixturePhysicalShardIdentity.ReadIdentity(Root);
    }

    internal Task SaveFailureDiagnosticsAsync(CancellationToken cancellationToken) =>
        RequireDiagnostics().SaveAsync(peerSecret, cancellationToken);

    private ClusterFixtureDiagnostics RequireDiagnostics() => diagnostics
        ?? throw new InvalidOperationException(DiagnosticsUnavailable);

    internal ContainerRuntimeControl RequireContainerRuntime() => containerRuntime
        ?? throw new InvalidOperationException(RuntimeControllerUnavailable);

    private DistributedApplication RequireApp() => App
        ?? throw new InvalidOperationException(RuntimeControllerUnavailable);
}
