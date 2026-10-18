using System.Net;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Runs the production server composition with native Orleans membership and silo-local storage.</summary>
internal sealed class RequestCqrsHostedRuntimeFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string DirectoryPrefix = "keyload-cqrs-host-";
    private const string GuidFormat = "N";
    private const string NotInitialized = "The production CQRS host is not initialized.";
    private const string Unjoined = "The production CQRS host has not joined its storage borrowers.";
    private const int OnePort = 1;
    private const int NoFailures = 0;
    private readonly Lock lifecycle = new();
    private readonly RequestCqrsHostedProfile profile;
    private readonly TestClusterPortAllocator ports = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private WebApplication? application;
    private OrleansNode? node;
    private Task? disposal;

    internal RequestCqrsHostedRuntimeFixture(TimeProvider? clock = null, GrainRoutingOptions? routing = null,
        IGrainRequestPhaseObserver? observer = null, IGrainRequestPurposeObserver? purposeObserver = null,
        bool nativeTextMaintenance = false, bool nativeAnnMaintenance = false, DatabaseLimits? limits = null,
        RequestCqrsHostedClusterNode? clusterNode = null)
    {
        profile = new(clock ?? TimeProvider.System, UnitRoutingOptions.Routing(routing), observer, purposeObserver,
            nativeTextMaintenance, nativeAnnMaintenance, limits, clusterNode);
    }

    internal IGrainFactory Grains => Services.GetRequiredService<IGrainFactory>();
    internal Guid ConnectionId => Services.GetRequiredService<NativeConnectionOwnerIdentity>().Id;
    internal NativeRuntimeTestOptions Timing => profile.Timing;
    internal TimeProvider Clock => profile.Clock;
    internal IOptions<GrainRoutingOptions> RoutingOptions => profile.Routing;
    internal IServiceProvider Services => application?.Services ?? throw new InvalidOperationException(NotInitialized);
    private PartitionHost Local => Services.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
    internal GrainRequestCodec Codec => Services.GetRequiredService<GrainRequestCodec>();
    internal NativeRequestWorkOwner RequestWork => (node ?? throw new InvalidOperationException(NotInitialized)).RequestWork;
    internal NativeTextIncrementalMaintenanceService? NativeMaintenance => profile.NativeText ? Local.TextMaintenance : null;
    internal NativeAnnMaintenanceService? NativeAnnMaintenance => profile.NativeAnn ? Local.AnnMaintenance : null;
    internal RequestCqrsHostedDatabase Database { get; private set; } = null!;
    internal bool IsDisposalJoined => disposal?.IsCompletedSuccessfully == true;
    internal bool OwnedDirectoryExists => Directory.Exists(directory);

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(Timing.StartupTimeout, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            var siloPort = profile.ClusterNode?.SiloPort ?? ports.AllocateConsecutivePortPairs(OnePort).Item1;
            var primary = profile.ClusterNode?.PrimaryEndpoint ?? new IPEndPoint(IPAddress.Loopback, siloPort);
            application = await ServerConfiguration.BuildAsync(profile.Arguments(directory, siloPort),
                localhostClustering: new(primary),
                phaseObserver: profile.Observer, purposeObserver: profile.PurposeObserver,
                configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
            node = Services.GetRequiredService<OrleansNode>();
            await application.StartAsync(startup.Token).ConfigureAwait(false);
            await node.StartAsync(startup.Token).ConfigureAwait(false);
            await Local.Coordinator.ReadBarrierAsync(startup.Token).ConfigureAwait(false);
            Database = new(Local);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        if (application is not null)
        {
            await CloseApplicationAsync(application, failures).ConfigureAwait(false);
        }
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(ports.Dispose, failures);
            if (Directory.Exists(directory))
            {
                ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CloseApplicationAsync(WebApplication app, List<Exception> failures)
    {
        using var deadline = new CancellationTokenSource(Timing.ShutdownTimeout, TimeProvider.System);
        var connections = Services.GetRequiredService<ServerConnectionRegistry>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (node is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => node.StopAsync(deadline.Token), failures).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
            if (!node.HasJoinedRequestWork)
            {
                failures.Add(new InvalidOperationException(Unjoined));
                return;
            }
        }
        await ServerFailureObserver.ObserveAsync(() => app.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => Services.GetRequiredService<PartitionStorageLifecycle>()
            .StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var owner = Services.GetRequiredService<PartitionHostApplicationOwner>();
        if (owner.Closing is not { IsCompleted: true } || !owner.CanReleaseRoot)
        {
            failures.Add(new InvalidOperationException(Unjoined));
            return;
        }
        await ServerFailureObserver.ObserveAsync(() => app.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
    }
}
