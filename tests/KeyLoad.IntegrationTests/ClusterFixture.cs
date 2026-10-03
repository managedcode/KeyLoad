using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.TestInfrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace KeyLoad.IntegrationTests;

/// <summary>Owns the real Aspire RF3 application shared by public SDK integration scenarios.</summary>
internal sealed class ClusterFixture : IAsyncInitializer, IAsyncDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private readonly long? commandBytes;
    private readonly HttpAdmissionLimits? httpAdmission;
    private ClusterFixtureDiagnostics? diagnostics;
    private ContainerRuntimeControl? containerRuntime;
    private byte[] peerSecret = [];

    private const string InvalidContainerModel = "The Aspire model must expose exactly the three named RF3 container resources.";
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

    /// <summary>Gets the unique private host directory used by the Aspire application.</summary>
    public string Root { get; } = Path.Combine(Path.GetTempPath(),
        ClusterFixtureProtocol.RootDirectoryPrefix + Guid.NewGuid().ToString(ClusterFixtureProtocol.GuidFormat));

    /// <summary>Gets the actual started Aspire application and its allocated endpoint model.</summary>
    public DistributedApplication App { get; private set; } = null!;

    /// <summary>Gets the administrator credential loaded from the private local profile.</summary>
    public string AdminKey { get; private set; } = "";

    /// <summary>Starts all three genuine Aspire resources and waits for their health concurrently.</summary>
    /// <returns>A task that completes when each of the three resources is healthy.</returns>
    public async Task InitializeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(StartupTimeout);
            var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.KeyLoad_AppHost>(
                [$"{ClusterFixtureProtocol.DataRootArgument}{Root}", ClusterFixtureProtocol.EphemeralArgument,
                    ClusterFixtureProtocol.SnapshotThresholdArgument], timeout.Token);
            ConfigureCommandAdmission(builder, commandBytes);
            ConfigureHttpAdmission(builder);
            var containerNames = GetContainerNames(builder);
            var repository = ClusterFixtureDiagnostics.FindRepositoryRoot();
            ConfigureLogging(builder);

            App = await builder.BuildAsync(timeout.Token);
            containerRuntime = new(App, containerNames, repository.FullName);
            diagnostics = new(App);
            diagnostics.Start(App.Services.GetRequiredService<ResourceLoggerService>());
            await ClusterFixtureImageIdentity.VerifyAsync(App, timeout.Token);
            await App.StartAsync(timeout.Token);
            ReadPrivateProfile();

            await Task.WhenAll(Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
                .Select(number => App.ResourceNotifications.WaitForResourceHealthyAsync(ClusterFixtureProtocol.NodeName(number), timeout.Token)));
        }
        catch (Exception startupFailure)
        {
            await ClusterFixtureStartupFailure.DisposeAndThrowAsync(this, startupFailure).ConfigureAwait(false);
            throw;
        }
    }

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
            var client = new KeyLoadClient(pending, key ?? AdminKey);
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
        var ownedDiagnostics = diagnostics;
        var cleanupFailures = new List<Exception>();
        if (ownedDiagnostics is not null)
        {
            await ClusterFixtureCleanup.CollectFailureAsync(
                ownedDiagnostics.SaveAsync(peerSecret, ownedDiagnostics.LifetimeToken), cleanupFailures).ConfigureAwait(false);
            await ClusterFixtureCleanup.CollectFailureAsync(
                diagnostics!.DisposeAsync().AsTask(), cleanupFailures).ConfigureAwait(false);
            diagnostics = null;
        }

        var ownedApp = App;
        App = null!;
        if (ownedApp is not null)
        {
            await ClusterFixtureCleanup.CollectFailureAsync(() => ownedApp.StopAsync(), cleanupFailures).ConfigureAwait(false);
            await ClusterFixtureCleanup.CollectFailureAsync(() => ownedApp.DisposeAsync().AsTask(), cleanupFailures).ConfigureAwait(false);
        }

        if (Directory.Exists(Root))
        {
            await ClusterFixtureCleanup.CollectFailureAsync(() => DeleteRootAsync(Root), cleanupFailures).ConfigureAwait(false);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("Cluster fixture cleanup failed.", cleanupFailures);
        }
    }

    private static Task DeleteRootAsync(string root)
    {
        Directory.Delete(root, recursive: true);
        return Task.CompletedTask;
    }

    private static void ConfigureCommandAdmission(IDistributedApplicationTestingBuilder builder, long? commandBytes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (commandBytes is not { } bytes)
        {
            return;
        }

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.CommandBytesSetting,
                bytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private void ConfigureHttpAdmission(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (httpAdmission is not { } limits)
        {
            return;
        }

        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithEnvironment(ClusterFixtureProtocol.HttpBodyBytesSetting,
                limits.MaxBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpControlBodyBytesSetting,
                limits.MaxControlBodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpReservedBytesSetting,
                limits.MaxReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
            node.WithEnvironment(ClusterFixtureProtocol.HttpHeavyReadBytesSetting,
                limits.HeavyReadReservedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static Dictionary<string, string> GetContainerNames(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var names = builder.Resources.OfType<ContainerResource>()
            .Where(resource => ClusterFixtureProtocol.IsNodeName(resource.Name))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
        if (names.Count != ClusterFixtureProtocol.NodeCount
            || Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
                .Any(number => !names.ContainsKey(ClusterFixtureProtocol.NodeName(number))))
        {
            throw new InvalidOperationException(InvalidContainerModel);
        }

        return names;
    }

    private static void ConfigureLogging(IDistributedApplicationTestingBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
            logging.AddFilter(ClusterFixtureProtocol.HealthCheckLoggerCategory, LogLevel.Critical);
        });
    }

    private void ReadPrivateProfile()
    {
        var profilePath = Path.Combine(Root, ClusterFixtureProtocol.ProfileFileName);
        using var profile = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(profilePath));
        AdminKey = profile.RootElement.GetProperty(ClusterFixtureProtocol.AdminKeyProperty).GetString()!;
        peerSecret = Convert.FromBase64String(profile.RootElement.GetProperty(ClusterFixtureProtocol.PeerSecretProperty).GetString()!);
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
