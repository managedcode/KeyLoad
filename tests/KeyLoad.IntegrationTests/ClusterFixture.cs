using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ClusterFixtureResourceCleanup;

namespace KeyLoad.IntegrationTests;

/// <summary>Owns the real Aspire Orleans application shared by public SDK integration scenarios.</summary>
internal sealed class ClusterFixture : IAsyncInitializer, IAsyncDisposable, IKeyedDataSource
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private readonly RemoteTransferCoordinatorFixtureSelection? transferCoordinator;
    private readonly long? commandBytes;
    private readonly Features.Authorization.FoundationOtlpClusterSelection? telemetry;
    private readonly Features.Messaging.QueueDeadlineClusterSelection? queueDeadline;
    private readonly Features.QueryExecution.PostgresClusterFixtureOwnership nativeSql = new();
    private readonly HttpAdmissionLimits? httpAdmission;
    private readonly DatabaseLimits? databaseLimits;
    private readonly QueryExecutionOptions? queryExecution;
    private ClusterFixtureDiagnostics? diagnostics;
    private ContainerRuntimeControl? containerRuntime;
    private NativeCoverageClusterFixtureOwner? coverage;
    private LocalClusterImageTestSession? localImageSession;
    private bool localImageCleanupIncomplete;
    private readonly string coverageFixtureId = Guid.NewGuid().ToString("D");
    /// <summary>Receives the native keyed data-source identity before initialization.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Creates the standard Orleans fixture for the shared TUnit class data source.</summary>
    public ClusterFixture()
    {
    }

    internal ClusterFixture(Features.Authorization.FoundationOtlpClusterSelection selection)
        => telemetry = Features.Authorization.FoundationOtlpClusterConfiguration.Validate(selection);

    internal ClusterFixture(RemoteTransferCoordinatorFixtureSelection selection)
        => transferCoordinator = RemoteTransferCoordinatorFixtureConfiguration.Validate(selection);

    internal ClusterFixture(Features.Messaging.QueueDeadlineClusterSelection queueDeadline)
        => this.queueDeadline = queueDeadline;

    internal ClusterFixture(Features.QueryExecution.PostgresClusterSelection selection)
        => nativeSql = new(selection);

    /// <summary>Creates an Orleans fixture with a smaller command admission budget.</summary>
    /// <param name="commandBytes">The per-node retained command byte limit.</param>
    internal ClusterFixture(long commandBytes) => this.commandBytes = commandBytes;

    /// <summary>Creates an Orleans fixture with explicit HTTP admission bounds.</summary>
    /// <param name="httpAdmission">The HTTP body and control-body limits for each node.</param>
    internal ClusterFixture(HttpAdmissionLimits httpAdmission)
        => this.httpAdmission = ClusterFixtureRuntimeInvocation.RequireAdmission(httpAdmission);

    /// <summary>Creates an Orleans fixture with the server's typed database execution limits.</summary>
    /// <param name="databaseLimits">The validated limits applied to every Aspire Orleans node.</param>
    internal ClusterFixture(DatabaseLimits databaseLimits)
        => this.databaseLimits = ClusterFixtureDatabaseLimits.Validate(databaseLimits);

    /// <summary>Creates an Orleans fixture with explicit database and additional query result bounds.</summary>
    internal ClusterFixture(DatabaseLimits databaseLimits, QueryExecutionOptions queryExecution)
        : this(databaseLimits)
        => this.queryExecution = ClusterFixtureQueryResultLimits.Validate(queryExecution);

    /// <summary>Composes validated HTTP admission and query capabilities in the same Aspire Orleans cluster.</summary>
    internal ClusterFixture(HttpAdmissionLimits httpAdmission, DatabaseLimits databaseLimits, QueryExecutionOptions queryExecution)
        : this(databaseLimits, queryExecution)
        => this.httpAdmission = ClusterFixtureRuntimeInvocation.RequireAdmission(httpAdmission);

    /// <summary>Gets the unique private host directory used by the Aspire application.</summary>
    public string Root
    {
        get; private set;
    } = Path.Combine(Path.GetTempPath(),
        ClusterFixtureProtocol.RootDirectoryPrefix + Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat));

    /// <summary>Gets the actual started Aspire application and its allocated endpoint model.</summary>
    public DistributedApplication App { get; private set; } = null!;
    /// <summary>Gets the administrator credential loaded from the private local profile.</summary>
    public string AdminKey { get; private set; } = "";

    internal ClusterFixtureColdStartObservation? ColdStartObservation
    {
        get; private set;
    }

    /// <summary>Starts all three genuine Aspire resources and waits for their health concurrently.</summary>
    /// <returns>A task that completes when each of the three resources is healthy.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            using var preparation = new CancellationTokenSource(TimeSpan.FromMinutes(15), TimeProvider.System);
            localImageSession = await LocalClusterImageTestSession.StartIfSelectedAsync(preparation.Token)
                .ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(StartupTimeout, TimeProvider.System);
            coverage = await NativeCoverageClusterFixtureOwner.CreateAsync(coverageFixtureId, timeout.Token)
                .ConfigureAwait(false);
            var selection = ClusterFixtureComposition.PrepareArguments(Root, coverage, localImageSession?.Selection);
            Root = selection.Root;
            var arguments = selection.Arguments;
            ColdStartObservation = ClusterFixtureApplicationStartup.ObserveColdStart(Root);
            var builder = await ClusterFixtureComposition.CreateBuilderAsync(arguments, telemetry, timeout.Token);
            ClusterFixtureComposition.ConfigureTestOverrides(builder, commandBytes, httpAdmission, databaseLimits, queryExecution);
            queueDeadline?.Configure(builder);
            nativeSql.Configure(builder, Root);
            RemoteTransferCoordinatorFixtureConfiguration.Configure(builder, transferCoordinator);
            var containerNames = ClusterFixtureComposition.GetContainerNames(builder);
            var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
            ClusterFixtureComposition.ConfigureLogging(builder);
            App = await builder.BuildAsync(timeout.Token);
            containerRuntime = new(App, containerNames, repository.FullName, localImageSession?.Selection);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            AdminKey = ClusterFixtureProfile.ReadProfile(Root).AdminKey;
            await ClusterFixtureApplicationStartup.StartAsync(App, repository.FullName, containerNames,
                coverage, localImageSession?.Selection, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception startupFailure)
        {
            await ClusterFixtureFailureRootRetention.For(this).StartupFailureAsync(this, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

    internal void RegisterNativeCoverageCase<TCase>(string methodName) => coverage?.RegisterCase<TCase>(methodName);

    /// <summary>Creates the real .NET SDK client for one Aspire node and selected credential.</summary>
    /// <param name="node">The Aspire HTTP endpoint resource name.</param>
    /// <param name="key">An optional database credential; null selects the fixture administrator.</param>
    /// <returns>A client backed by Aspire's actual allocated HTTP endpoint.</returns>
    public KeyLoadClient Client(string node, string? key = null)
        => ClusterFixtureComposition.CreateClient(RequireApp(), node, key ?? AdminKey);

    /// <summary>Writes bounded Orleans state, readiness and recent node-log diagnostics.</summary>
    /// <returns>A task that completes after the diagnostic artifact is written.</returns>
    public Task SaveFailureDiagnosticsAsync() => SaveFailureDiagnosticsAsync(RequireDiagnostics().LifetimeToken);

    /// <summary>Kills the inspected Aspire-managed Docker container for a named test scenario.</summary>
    /// <param name="resourceName">The Aspire resource name to stop.</param>
    /// <param name="scenario">The stable scenario identity recorded in the kill receipt.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed real Docker kill operation.</returns>
    public Task KillContainerAsync(string resourceName, string scenario, CancellationToken cancellationToken)
        => ClusterFixtureRuntimeInvocation.KillAsync(RequireContainerRuntime, resourceName, scenario, cancellationToken);

    /// <summary>Restarts a previously killed Aspire-managed container and records its actual new generation.</summary>
    /// <param name="resourceName">The Aspire resource name to restart.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed native Aspire resource start operation.</returns>
    public Task RestartContainerAsync(string resourceName, CancellationToken cancellationToken)
        => ClusterFixtureRuntimeInvocation.RestartAsync(RequireContainerRuntime, resourceName, cancellationToken);

    /// <summary>Saves diagnostics, drains observers and disposes the actual Aspire application.</summary>
    /// <returns>A task that completes after the owned test resources are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        using var deadline = coverage?.CreateCleanupDeadline();
        using var localCleanup = localImageSession?.CreateApplicationCleanupCancellation();
        var token = deadline?.Token ?? localCleanup?.Token ?? CancellationToken.None;
        var failures = new List<Exception>();
        await SaveDiagnosticsAsync(diagnostics, deadline, failures).ConfigureAwait(false);
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
        var ownedLocalImage = localImageSession is not null;
        if (localImageSession is not null)
        {
            await ClusterFixtureLocalImageLifecycle.JoinAsync(localImageSession, Root, appStopped, ownedApp is null,
                failures).ConfigureAwait(false);
            localImageSession = null;
        }
        localImageCleanupIncomplete |= ownedLocalImage && failures.Count > 0;
        localImageCleanupIncomplete = await Features.QueryExecution.PostgresClusterFixtureOwnership.ReleaseRootAsync(
            localImageCleanupIncomplete, failures,
            () => ClusterFixtureFailureRootRetention.For(this).ReleaseAsync(
                () => DeleteOwnedRootAsync(coverage, Root, deadline, failures))).ConfigureAwait(false);
        await ClusterFixtureFailureRootRetention.For(this).CompleteAsync(Root,
            () => new ClusterFixtureCleanupCompletion(() => coverage, Rethrow)
                .CompleteAsync(appStopped, stoppedNodes, deadline, failures, token), failures).ConfigureAwait(false);
    }

    internal Task<string> SaveFailureDiagnosticsAsync(CancellationToken cancellationToken) =>
        RequireDiagnostics().SaveAsync(cancellationToken);

    private ClusterFixtureDiagnostics RequireDiagnostics() => diagnostics
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.DiagnosticsUnavailable);

    internal ContainerRuntimeControl RequireContainerRuntime() => containerRuntime
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.RuntimeControllerUnavailable);

    private DistributedApplication RequireApp() => App
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.RuntimeControllerUnavailable);
}
