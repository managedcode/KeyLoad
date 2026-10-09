using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one real production application, silo and physical stores; borrowed operation never owns them.</summary>
internal sealed class PartitionMovementLateNativeNode : IAsyncDisposable
{
    internal WebApplication Application { get; }
    internal TimeSpan ShutdownTimeout { get; }
    internal TimeProvider Clock { get; }
    private PartitionHost? partition;
    private OrleansNode? silo;
    internal PartitionHost Partition => partition ??= Application.Services.GetRequiredService<PartitionHost>();
    internal OrleansNode Silo => silo ??= Application.Services.GetRequiredService<OrleansNode>();
    internal bool IsDisposed { get; private set; }
    private Task? shutdown;
    private Task? applicationDisposal;
    private Task? partitionDisposal;

    internal PartitionMovementLateNativeNode(string[] arguments, IGrainPartitionMovementSealedOperationObserver? observer,
        IControlledBlobWireBorrowObserver? blobWireObserver = null)
    {
        Application = ServerConfiguration.Build(arguments, observer, blobWireObserver);
        ShutdownTimeout = Application.Services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value.ShutdownTimeout;
        Clock = Application.Services.GetRequiredService<TimeProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
        { return; }
        if (shutdown is { } original)
        { await original.ConfigureAwait(false); return; }
        using var deadline = new CancellationTokenSource(ShutdownTimeout, Clock);
        await StopAsync(deadline.Token).ConfigureAwait(false);
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await Partition.Coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
        await Application.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task StopAsync(CancellationToken cancellationToken) => shutdown ??= StopCoreAsync(cancellationToken);

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        { return; }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Application.Lifetime.StopApplication, failures);
        if (!await JoinSiloAsync(failures, cancellationToken).ConfigureAwait(false))
        { ServerFailureObserver.ThrowIfAny(failures); return; }
        await CloseApplicationAndStorageAsync(failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        IsDisposed = true;
    }

    private async Task<bool> JoinSiloAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        if (silo is not { } actualSilo)
        { return true; }
        Task? stopped = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = actualSilo.StopAsync(cancellationToken);
            await stopped.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (stopped is { IsCompletedSuccessfully: true } && actualSilo.HasJoinedRequestWork)
        { return true; }
        failures.Add(new InvalidOperationException("The native silo host and request owners have not both joined; retain storage."));
        return false;
    }

    private async Task CloseApplicationAndStorageAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        Task? applicationStop = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationStop = Application.StopAsync(cancellationToken);
            await applicationStop.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            applicationDisposal ??= Application.DisposeAsync().AsTask();
            await applicationDisposal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (applicationStop is not { IsCompletedSuccessfully: true }
            && applicationDisposal is not { IsCompletedSuccessfully: true })
        {
            failures.Add(new InvalidOperationException("The HTTP application has not joined; retain its borrowed storage."));
            return;
        }
        if (partition is { } actualPartition)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                partitionDisposal ??= actualPartition.DisposeAsync().AsTask();
                await partitionDisposal.WaitAsync(cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
    }
}
