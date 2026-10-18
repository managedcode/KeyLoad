using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Client;
using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.Query;
using KeyLoad.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
    private ProcessRuntimeControl? processRuntime;
    private string? processEvidenceDirectory;
    private string[] resourceNames = [];
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
            using var timeout = new CancellationTokenSource(StartupTimeout, TimeProvider.System);
            var arguments = ClusterFixtureComposition.PrepareArguments(Root, PublicQueryStorageSelection.Read(Key));
            ColdStartObservation = ClusterFixtureApplicationStartup.ObserveColdStart(Root);
            var builder = await ClusterFixtureComposition.CreateBuilderAsync(arguments, telemetry, timeout.Token);
            ClusterFixtureComposition.ConfigureTestOverrides(builder, commandBytes, httpAdmission, databaseLimits, queryExecution);
            queueDeadline?.Configure(builder);
            nativeSql.Configure(builder, Root);
            RemoteTransferCoordinatorFixtureConfiguration.Configure(builder, transferCoordinator);
            resourceNames = ClusterFixtureComposition.GetResourceNames(builder);
            var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
            ClusterFixtureComposition.ConfigureLogging(builder);
            App = await builder.BuildAsync(timeout.Token);
            processRuntime = new(App, resourceNames, repository.FullName);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            AdminKey = ClusterFixtureProfile.ReadProfile(Root).AdminKey;
            processEvidenceDirectory = await ClusterFixtureApplicationStartup.StartAsync(App, resourceNames, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception startupFailure)
        {
            await ClusterFixtureFailureRootRetention.For(this).StartupFailureAsync(this, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Creates the real .NET SDK client for one Aspire node and selected credential.</summary>
    /// <param name="node">The Aspire HTTP endpoint resource name.</param>
    /// <param name="key">An optional database credential; null selects the fixture administrator.</param>
    /// <returns>A client backed by Aspire's actual allocated HTTP endpoint.</returns>
    public KeyLoadClient Client(string node, string? key = null)
        => ClusterFixtureComposition.CreateClient(RequireApp(), node, key ?? AdminKey);

    /// <summary>Writes bounded Orleans state, readiness and recent node-log diagnostics.</summary>
    /// <returns>A task that completes after the diagnostic artifact is written.</returns>
    public Task SaveFailureDiagnosticsAsync() => SaveFailureDiagnosticsAsync(RequireDiagnostics().LifetimeToken);

    /// <summary>Abruptly kills the inspected Aspire-owned process for a named test scenario.</summary>
    /// <param name="resourceName">The Aspire resource name to stop.</param>
    /// <param name="scenario">The stable scenario identity recorded in the kill receipt.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed joined abrupt process kill.</returns>
    public Task KillServerAsync(string resourceName, string scenario, CancellationToken cancellationToken)
        => ClusterFixtureRuntimeInvocation.KillAsync(RequireProcessRuntime, resourceName, scenario, cancellationToken);

    /// <summary>Restarts a previously killed Aspire-owned process and records its actual new generation.</summary>
    /// <param name="resourceName">The Aspire resource name to restart.</param>
    /// <param name="cancellationToken">The bounded test operation cancellation.</param>
    /// <returns>The completed native Aspire resource start operation.</returns>
    public Task RestartServerAsync(string resourceName, CancellationToken cancellationToken)
        => ClusterFixtureRuntimeInvocation.RestartAsync(RequireProcessRuntime, resourceName, cancellationToken);

    /// <summary>Saves diagnostics, drains observers and disposes the actual Aspire application.</summary>
    /// <returns>A task that completes after the owned test resources are disposed.</returns>
    public async ValueTask DisposeAsync()
    {
        using var cleanup = new CancellationTokenSource(App is null ? TimeSpan.FromSeconds(30)
            : App.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value.ApplicationCleanupTimeout,
            TimeProvider.System);
        var failures = new List<Exception>();
        await SaveDiagnosticsAsync(diagnostics, failures, cleanup.Token).ConfigureAwait(false);
        if (diagnostics is not null)
        {
            try
            {
                await diagnostics.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            diagnostics = null;
        }
        var ownedApp = App;
        App = null!;
        var stopped = await ClusterFixtureApplicationShutdown.CollectAsync(ownedApp, resourceNames, processEvidenceDirectory,
            failures, cleanup.Token)
            .ConfigureAwait(false);
        _ = await Features.QueryExecution.PostgresClusterFixtureOwnership.ReleaseRootAsync(!stopped, failures,
            () => ClusterFixtureFailureRootRetention.For(this).ReleaseAsync(
                () => DeleteOwnedRootAsync(Root, failures))).ConfigureAwait(false);
        await ClusterFixtureFailureRootRetention.For(this).CompleteAsync(Root, () =>
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return Task.CompletedTask;
        }, failures).ConfigureAwait(false);
    }

    internal Task<string> SaveFailureDiagnosticsAsync(CancellationToken cancellationToken) =>
        RequireDiagnostics().SaveAsync(cancellationToken);

    private ClusterFixtureDiagnostics RequireDiagnostics() => diagnostics
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.DiagnosticsUnavailable);

    internal ProcessRuntimeControl RequireProcessRuntime() => processRuntime
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.RuntimeControllerUnavailable);

    private DistributedApplication RequireApp() => App
        ?? throw new InvalidOperationException(ClusterFixtureRuntimeInvocation.RuntimeControllerUnavailable);
}
