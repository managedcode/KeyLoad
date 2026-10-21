using KeyLoad.Clustering;
using KeyLoad.RecoveryTests.Features.StorageRecovery;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.TestingHost;
using TUnit.Core.Interfaces;

namespace KeyLoad.RecoveryTests.Features.OrleansExecution;

/// <summary>Runs the production server composition with execution Orleans membership and silo-local storage.</summary>
[ConfigurationBinding]
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
    private readonly string directory = Path.Combine(StorageTrialDirectory.ResolveExistingDirectory(new(AppContext.BaseDirectory)),
        DirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
    private WebApplication? application;
    private string[]? arguments;
    private int? reservedSiloPort;
    private Task? stopping;
    private Task? disposal;
    private bool retainDirectory;

    internal RequestCqrsHostedRuntimeFixture(TimeProvider? clock = null, GrainRoutingOptions? routing = null,
        IGrainRequestPhaseObserver? observer = null, IGrainRequestPurposeObserver? purposeObserver = null,
        bool textMaintenance = false, bool annMaintenance = false, DatabaseLimits? limits = null,
        RequestCqrsHostedClusterNode? clusterNode = null,
        Func<IServiceProvider, ILifecycleParticipant<ISiloLifecycle>>? lifecycleParticipantFactory = null,
        IDatabaseAuthenticationExecutionObserver? authenticationExecutionObserver = null,
        Action<IServiceCollection, NodeOptions>? configureServices = null)
    {
        profile = new(clock ?? TimeProvider.System, RequestCqrsHostedProfile.ValidateRouting(routing), observer, purposeObserver,
            textMaintenance, annMaintenance, limits, clusterNode, lifecycleParticipantFactory,
            authenticationExecutionObserver, configureServices);
    }

    internal IGrainFactory Grains => Services.GetRequiredService<IGrainFactory>();
    internal Guid ConnectionId => Services.GetRequiredService<ConnectionOwnerIdentity>().Id;
    internal RuntimeTestOptions Timing => profile.Timing;
    internal TimeProvider Clock => profile.Clock;
    internal IOptions<GrainRoutingOptions> RoutingOptions => profile.Routing;
    internal IServiceProvider Services => application?.Services ?? throw new InvalidOperationException(NotInitialized);
    internal IServiceProvider PhysicalServices => Services.GetRequiredService<PartitionHostApplicationOwner>().Session.Services;
    private PartitionHost Local => Services.GetRequiredService<PartitionHostApplicationOwner>().Borrow();
    internal GrainRequestCodec Codec => Services.GetRequiredService<GrainRequestCodec>();
    internal RequestWorkOwner RequestWork => Services.GetRequiredService<OrleansNode>().RequestWork;
    internal TextIncrementalMaintenanceService? RuntimeMaintenance => profile.RuntimeText ? Local.TextMaintenance : null;
    internal AnnMaintenanceService? RuntimeAnnMaintenance => profile.RuntimeAnn ? Local.AnnMaintenance : null;
    internal RequestCqrsHostedDatabase Database { get; private set; } = null!;
    internal bool IsDisposalJoined => disposal?.IsCompletedSuccessfully == true;
    internal bool OwnedDirectoryExists => Directory.Exists(directory);
    internal bool IsStopJoined => stopping?.IsCompletedSuccessfully == true;
    internal Uri PublicEndpoint => new((application ?? throw new InvalidOperationException(NotInitialized)).Urls.Single());
    internal string AdminKey => Services.GetRequiredService<IOptions<NodeOptions>>().Value.AdminKey;
    internal void RetainRoot() => Volatile.Write(ref retainDirectory, true);

    public Task InitializeAsync() => InitializeAsync(CancellationToken.None);

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await StartApplicationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            await ServerFailureObserver.ObserveAsync(() => StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task owned;
        lock (lifecycle)
        {
            owned = stopping ??= StopApplicationAsync();
        }
        await owned.ConfigureAwait(false);
    }

    internal async Task RestartAsync(CancellationToken cancellationToken)
    {
        await StopAsync(cancellationToken).ConfigureAwait(false);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposal is not null, this);
            stopping = null;
        }
        try
        {
            await StartApplicationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            await ServerFailureObserver.ObserveAsync(() => StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private async Task StartApplicationAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposal is not null, this);
        if (application is not null || stopping is not null)
        {
            throw new InvalidOperationException("The execution CQRS host requires a joined restart before another initialization.");
        }
        using var deadline = new CancellationTokenSource(Timing.StartupTimeout, TimeProvider.System);
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        reservedSiloPort ??= profile.ClusterNode?.SiloPort ?? ports.AllocateConsecutivePortPairs(OnePort).Item1;
        var siloPort = reservedSiloPort.Value;
        arguments ??= profile.Arguments(directory, siloPort);
        application = await ServerConfiguration.BuildAsync(arguments,
            authenticationExecutionObserver: profile.AuthenticationExecutionObserver,
            lifecycleParticipantFactory: profile.LifecycleParticipantFactory,
            phaseObserver: profile.Observer, purposeObserver: profile.PurposeObserver,
            configureServices: profile.Register, startupCancellation: startup.Token).ConfigureAwait(false);
        await application.StartAsync(startup.Token).ConfigureAwait(false);
        await Local.Coordinator.ReadBarrierAsync(startup.Token).ConfigureAwait(false);
        Database = new(Local);
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
        await ServerFailureObserver.ObserveAsync(() => StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (failures.Count == NoFailures)
        {
            try
            {
                ports.Dispose();
            }
            catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            if (!Volatile.Read(ref retainDirectory) && Directory.Exists(directory))
            {
                ServerFailureObserver.Observe(() => Directory.Delete(directory, recursive: true), failures);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task StopApplicationAsync()
    {
        if (application is null)
        {
            return;
        }
        var failures = new List<Exception>();
        await CloseApplicationAsync(application, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        application = null;
        Database = null!;
    }

    private async Task CloseApplicationAsync(WebApplication app, List<Exception> failures)
    {
        using var deadline = new CancellationTokenSource(Timing.ShutdownTimeout, TimeProvider.System);
        var connections = Services.GetRequiredService<ServerConnectionRegistry>();
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures).ConfigureAwait(false);
        var node = app.Services.GetRequiredService<OrleansNode>();
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(deadline.Token), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        if (!node.HasJoinedRequestWork)
        {
            failures.Add(new InvalidOperationException(Unjoined));
            return;
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
