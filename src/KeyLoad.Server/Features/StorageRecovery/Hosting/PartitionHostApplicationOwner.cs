using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Server;

/// <summary>Application-owned original physical opening and shutdown; DI only borrows publication.</summary>
internal sealed class PartitionHostApplicationOwner(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
    CommandAdmissionGovernor admission, TimeProvider clock, ILogger<ReplicaConsensus> logger) : IAsyncDisposable
{
    private const string Unpublished = "The original physical host opening has not completed successfully.";
    private readonly Lock lifecycle = new();
    private readonly PartitionHostOwnership ownership = new();
    private PartitionHost? host;
    private Task? opening;
    private Task? shutdown;
    private bool published;
    private bool stopping;

    internal PartitionHost Borrow()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return published && host is not null ? host : throw new InvalidOperationException(Unpublished);
        }
    }

    internal Task OpenAsync()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return opening ??= OpenCoreAsync();
        }
    }

    private async Task OpenCoreAsync()
    {
        var opened = await PartitionHost.OpenAsync(options, authorization, admission, clock, logger, ownership)
            .ConfigureAwait(false);
        lock (lifecycle)
        {
            host = opened;
            published = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            stopping = true;
            return new(shutdown ??= CloseCoreAsync());
        }
    }

    private async Task CloseCoreAsync()
    {
        var failures = new List<Exception>();
        if (opening is { } original)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
