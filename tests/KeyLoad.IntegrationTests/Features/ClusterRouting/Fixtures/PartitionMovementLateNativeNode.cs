using KeyLoad.Clustering;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one real production application; its native silo lifecycle owns physical storage.</summary>
internal sealed partial class PartitionMovementLateNativeNode : IAsyncDisposable
{
    private const string Unpublished = "The native application has not published its original storage and runtime.";
    internal WebApplication Application
    {
        get;
    }
    internal TimeSpan ShutdownTimeout
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }
    private readonly Lock lifecycle = new();
    private Func<PartitionHost>? partition;
    private readonly Func<OrleansNode> silo;
    private Task? startup;
    private Task? shutdown;
    private Task? applicationDisposal;
    private Task? storageStop;
    private readonly ServerConnectionRegistry connections;
    private readonly PartitionStorageLifecycle storageLifecycle;
    private readonly Func<PartitionHostApplicationOwner> storageOwner;
    private Func<CancellationToken, Task>? closeColdCohort;
    internal PartitionHost Partition => partition?.Invoke() ?? throw new InvalidOperationException(Unpublished);
    internal OrleansNode Silo => partition is not null
        ? silo() : throw new InvalidOperationException(Unpublished);
    internal bool IsDisposed
    {
        get; private set;
    }
    internal bool HasPublishedStorage => partition is not null;

    private PartitionMovementLateNativeNode(WebApplication application)
    {
        Application = application;
        ShutdownTimeout = application.Services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value.ShutdownTimeout;
        Clock = application.Services.GetRequiredService<TimeProvider>();
        connections = application.Services.GetRequiredService<ServerConnectionRegistry>();
        var originalSilo = application.Services.GetRequiredService<OrleansNode>();
        silo = () => originalSilo;
        storageLifecycle = application.Services.GetRequiredService<PartitionStorageLifecycle>();
        var originalStorageOwner = application.Services.GetRequiredService<PartitionHostApplicationOwner>();
        storageOwner = () => originalStorageOwner;
    }

    internal static async Task<PartitionMovementLateNativeNode> CreateAsync(string[] arguments,
        IGrainPartitionMovementSealedOperationObserver? observer, IControlledBlobWireBorrowObserver? blobWireObserver = null,
        NativeLocalhostClusteringOptions? localhostClustering = null, CancellationToken startupCancellation = default)
    {
        var application = await ServerConfiguration.BuildAsync(arguments, observer, blobWireObserver, localhostClustering,
            startupCancellation: startupCancellation).ConfigureAwait(false);
        try
        {
            return new(application);
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal Task OpenAsync(CancellationToken cancellationToken)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(shutdown is not null, this);
            return startup ??= OpenCoreAsync(cancellationToken);
        }
    }

    private async Task OpenCoreAsync(CancellationToken cancellationToken)
    {
        await Application.StartAsync(cancellationToken).ConfigureAwait(false);
        var opened = storageOwner().Borrow();
        var native = silo();
        ControlledPartitionMovementNativePublication.RequireActive(Application, opened, native);
        partition = () => opened;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await OpenAsync(cancellationToken).ConfigureAwait(false);
        await Silo.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.WaitForPhysicalOwnerRegistrationAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void BindColdCohort(Func<CancellationToken, Task> close)
    {
        ArgumentNullException.ThrowIfNull(close);
        if (closeColdCohort is not null || partition is null)
        {
            throw new InvalidOperationException(Unpublished);
        }
        closeColdCohort = close;
    }

    internal Task StopAsync(CancellationToken cancellationToken)
        => closeColdCohort is { } close ? close(cancellationToken) : StopLocalAsync(cancellationToken);

    internal Task StopLocalAsync(CancellationToken cancellationToken)
    {
        Task original;
        lock (lifecycle)
        {
            original = shutdown ??= StopCoreAsync();
        }
        return JoinShutdownAsync(original, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(ShutdownTimeout, Clock);
        await StopAsync(deadline.Token).ConfigureAwait(false);
    }
}
