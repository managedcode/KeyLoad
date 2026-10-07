using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.CodeQuality;
using KeyLoad.IntegrationTests.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core.Interfaces;

namespace KeyLoad.IntegrationTests;

/// <summary>Owns the real Aspire RF3 application shared by public SDK integration scenarios.</summary>
internal sealed class ClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private readonly long? commandBytes;
    private readonly HttpAdmissionLimits? httpAdmission;
    private readonly DatabaseLimits? databaseLimits;
    private ClusterFixtureDiagnostics? diagnostics;
    private ContainerRuntimeControl? containerRuntime;
    private NativeCoverageRf3FixtureOwner? coverage;
    private readonly string coverageFixtureId = Guid.NewGuid().ToString("D");
    private byte[] peerSecret = [];

    private const string RuntimeControllerUnavailable = "The Aspire container runtime controller is not initialized.";
    private const string DiagnosticsUnavailable = "The Aspire diagnostic capture is not initialized.";

    /// <summary>Creates the standard RF3 fixture for the shared TUnit class data source.</summary>
    public ClusterFixture() { }

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

    /// <summary>Gets the unique private host directory used by the Aspire application.</summary>
    public string Root { get; private set; } = Path.Combine(Path.GetTempPath(),
        ClusterFixtureProtocol.RootDirectoryPrefix + Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat));

    /// <summary>Gets the actual started Aspire application and its allocated endpoint model.</summary>
    public DistributedApplication App { get; private set; } = null!;
    /// <summary>Gets the administrator credential loaded from the private local profile.</summary>
    public string AdminKey { get; private set; } = "";

    /// <summary>Gets the opaque physical-shard identity from the actual private shared V2 profile.</summary>
    public Guid PhysicalShardId { get; private set; }

    /// <summary>Starts all three genuine Aspire resources and waits for their health concurrently.</summary>
    /// <returns>A task that completes when each of the three resources is healthy.</returns>
    public async Task InitializeAsync()
    {
        try
        {
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
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
                arguments, timeout.Token);
            ClusterFixtureComposition.ConfigureTestOverrides(builder, commandBytes, httpAdmission, databaseLimits);
            var containerNames = ClusterFixtureComposition.GetContainerNames(builder);
            var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
            ClusterFixtureComposition.ConfigureLogging(builder);
            App = await builder.BuildAsync(timeout.Token);
            containerRuntime = new(App, containerNames, repository.FullName);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            await StartApplicationAsync(containerNames, repository.FullName, timeout.Token).ConfigureAwait(false);
            ReadPrivateProfile();
        }
        catch (Exception startupFailure)
        {
            await ClusterFixtureStartupFailure.DisposeAndThrowAsync(this, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

    private async Task StartApplicationAsync(IReadOnlyDictionary<string, string> containerNames,
        string repository, CancellationToken token)
    {
        var localImageIdentity = coverage is null
            ? await LocalRf3ImageIdentity.VerifyBeforeStartAsync(App, repository, token).ConfigureAwait(false)
            : null;
        if (coverage is null && localImageIdentity is null)
        {
            await ClusterFixtureImageIdentity.VerifyAsync(App, token).ConfigureAwait(false);
        }
        else if (coverage is not null)
        {
            await coverage.VerifyBeforeStartAsync(App, token).ConfigureAwait(false);
        }
        await App.StartAsync(token).ConfigureAwait(false);
        var readinessNodes = Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
            .Select(ClusterFixtureProtocol.NodeName);
        await AspireStartupReadiness.WaitForHealthyAsync(App, readinessNodes, token).ConfigureAwait(false);
        if (localImageIdentity is not null)
        {
            await LocalRf3ImageIdentity.VerifyStartedContainersAsync(localImageIdentity, containerNames, token)
                .ConfigureAwait(false);
        }
        if (coverage is not null)
        {
            await coverage.VerifyStartedAsync(containerNames, token).ConfigureAwait(false);
        }
    }

    internal void RegisterNativeCoverageCase<TCase>(string methodName) => coverage?.RegisterCase<TCase>(methodName);

    /// <summary>Creates the real .NET SDK client for one Aspire node and selected credential.</summary>
    /// <param name="node">The Aspire HTTP endpoint resource name.</param>
    /// <param name="key">An optional database credential; null selects the fixture administrator.</param>
    /// <returns>A client backed by Aspire's actual allocated HTTP endpoint.</returns>
    public KeyLoadClient Client(string node, string? key = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        HttpClient? pending = null;
        try
        {
            pending = RequireApp().CreateHttpClient(node, ClusterFixtureProtocol.HttpEndpointName);
            pending.Timeout = ClusterFixtureProtocol.ClientTimeout;
            var client = new KeyLoadClient(pending, key ?? AdminKey, IntegrationClientOptions.Execution());
            pending = null;
            return client;
        }
        finally { pending?.Dispose(); }
    }

    /// <summary>Writes bounded RF3 state, signed discovery and recent node-log diagnostics.</summary>
    /// <returns>A task that completes after the diagnostic artifact is written.</returns>
    public Task SaveFailureDiagnosticsAsync() => SaveFailureDiagnosticsAsync(RequireDiagnostics().LifetimeToken);

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
        var token = deadline?.Token ?? CancellationToken.None;
        var failures = new List<Exception>();
        await SaveDiagnosticsAsync(deadline, failures).ConfigureAwait(false);
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
        await DeleteOwnedRootAsync(deadline, failures).ConfigureAwait(false);
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

    private async Task SaveDiagnosticsAsync(NativeCoverageCleanupDeadline? deadline, List<Exception> failures)
    {
        if (diagnostics is not { } owned)
        {
            return;
        }
        await CollectCleanupAsync(deadline,
            () => owned.SaveAsync(peerSecret, deadline?.Token ?? owned.LifetimeToken), failures).ConfigureAwait(false);
    }

    private async Task DeleteOwnedRootAsync(NativeCoverageCleanupDeadline? deadline, List<Exception> failures)
    {
        if (coverage is null)
        {
            if (Directory.Exists(Root))
            {
                await ClusterFixtureCleanup.CollectFailureAsync(() => DeleteRootAsync(Root), failures).ConfigureAwait(false);
            }
        }
        else if (Directory.Exists(Root) && failures.Count == 0)
        {
            await deadline!.CollectAsync(() => Task.Run(() => Directory.Delete(Root, recursive: true)), failures)
                .ConfigureAwait(false);
        }
    }

    private static void Rethrow(Exception failure) => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    private static Task DeleteRootAsync(string root)
    {
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private static Task CollectCleanupAsync(NativeCoverageCleanupDeadline? deadline, Func<Task> operation,
        List<Exception> failures)
        => deadline is null
            ? ClusterFixtureCleanup.CollectFailureAsync(operation, failures)
            : deadline.CollectAsync(operation, failures);
    private static Task CollectCleanupAsync(NativeCoverageCleanupDeadline? deadline, Task operation,
        List<Exception> failures)
        => deadline is null
            ? ClusterFixtureCleanup.CollectFailureAsync(operation, failures)
            : deadline.CollectAsync(operation, failures);
    private void ReadPrivateProfile()
    {
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(Root);
        AdminKey = profile.AdminKey;
        peerSecret = Convert.FromBase64String(profile.PeerSecret);
        PhysicalShardId = profile.PhysicalShardId;
    }

    private Task SaveFailureDiagnosticsAsync(CancellationToken cancellationToken) =>
        RequireDiagnostics().SaveAsync(peerSecret, cancellationToken);

    private ClusterFixtureDiagnostics RequireDiagnostics() => diagnostics
        ?? throw new InvalidOperationException(DiagnosticsUnavailable);

    private ContainerRuntimeControl RequireContainerRuntime() => containerRuntime
        ?? throw new InvalidOperationException(RuntimeControllerUnavailable);

    private DistributedApplication RequireApp() => App
        ?? throw new InvalidOperationException(RuntimeControllerUnavailable);
}
