using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Owns one real production application, silo and physical stores; borrowed operation never owns them.</summary>
internal sealed class PartitionMovementLateNativeNode : IAsyncDisposable
{
    private const string UnjoinedApplication = "The HTTP application has not joined; retain its borrowed storage.";
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
    private PartitionHost? partition;
    private OrleansNode? silo;
    internal PartitionHost Partition => partition ??= Application.Services.GetRequiredService<PartitionHost>();
    internal OrleansNode Silo => silo ??= Application.Services.GetRequiredService<OrleansNode>();
    internal bool IsDisposed
    {
        get; private set;
    }
    private Task? shutdown;
    private Task? applicationDisposal;
    private Task? partitionDisposal;

    internal PartitionMovementLateNativeNode(string[] arguments, IGrainPartitionMovementSealedOperationObserver? observer,
        IControlledBlobWireBorrowObserver? blobWireObserver = null, NativeLocalhostClusteringOptions? localhostClustering = null)
    {
        Application = ServerConfiguration.Build(arguments, observer, blobWireObserver, localhostClustering);
        ShutdownTimeout = Application.Services.GetRequiredService<IOptions<ServerExecutionOptions>>().Value.ShutdownTimeout;
        Clock = Application.Services.GetRequiredService<TimeProvider>();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed)
        {
            return;
        }
        if (shutdown is { } original)
        {
            await original.ConfigureAwait(false);
            return;
        }
        using var deadline = new CancellationTokenSource(ShutdownTimeout, Clock);
        await StopAsync(deadline.Token).ConfigureAwait(false);
    }

    internal Task OpenAsync() => PartitionHostApplicationOpening.OpenAsync(Application.Services);

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        await OpenAsync().ConfigureAwait(false);
        await Partition.Coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
        await Application.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.StartAsync(cancellationToken).ConfigureAwait(false);
        await Silo.WaitForPhysicalOwnerRegistrationAsync(cancellationToken).ConfigureAwait(false);
    }

    internal Task StopAsync(CancellationToken cancellationToken) => shutdown ??= StopCoreAsync(cancellationToken);

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return;
        }
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(Application.Lifetime.StopApplication, failures);
        if (!await JoinSiloAsync(failures, cancellationToken).ConfigureAwait(false))
        {
            ServerFailureObserver.ThrowIfAny(failures);
            return;
        }
        await CloseApplicationAndStorageAsync(failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        IsDisposed = true;
    }

    private async Task<bool> JoinSiloAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        if (silo is not { } actualSilo)
        {
            return true;
        }
        Task? stopped = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            stopped = actualSilo.StopAsync(cancellationToken);
            await stopped.ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (stopped is { IsCompletedSuccessfully: true } && actualSilo.HasJoinedRequestWork)
        {
            return true;
        }
        failures.Add(new InvalidOperationException("The native silo host and request owners have not both joined; retain storage."));
        return false;
    }

    private async Task CloseApplicationAndStorageAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        await ServerFailureObserver.ObserveAsync(() => Application.StopAsync(cancellationToken), failures).ConfigureAwait(false);
        if (!await JoinApplicationDisposalAsync(failures, cancellationToken).ConfigureAwait(false))
        {
            failures.Add(new InvalidOperationException(UnjoinedApplication));
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

    private async Task<bool> JoinApplicationDisposalAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        Task? original = null;
        ServerFailureObserver.Observe(() => original = applicationDisposal ??= Application.DisposeAsync().AsTask(), failures);
        if (original is null)
        {
            return false;
        }
        Task? bounded = null;
        ServerFailureObserver.Observe(() => bounded = original.WaitAsync(cancellationToken), failures);
        if (bounded is not null)
        {
            await bounded.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        await ObserveTerminalAsync(original, bounded, failures, cancellationToken).ConfigureAwait(false);
        return original.IsCompletedSuccessfully;
    }

    private static async Task ObserveTerminalAsync(Task original, Task? bounded,
        List<Exception> failures, CancellationToken cancellationToken)
    {
        var waiting = new List<Exception>();
        if (bounded is not null && !ReferenceEquals(bounded, original)
            && bounded.IsCanceled && cancellationToken.IsCancellationRequested)
        {
            await ServerFailureObserver.ObserveAsync(() => bounded, waiting).ConfigureAwait(false);
        }
        var terminal = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => original, terminal).ConfigureAwait(false);
        foreach (var canceled in waiting.OfType<OperationCanceledException>())
        {
            if (canceled.CancellationToken == cancellationToken
                && !terminal.OfType<OperationCanceledException>().Any(error => error.CancellationToken == cancellationToken))
            {
                failures.Add(canceled);
            }
        }
        failures.AddRange(terminal);
    }

}
