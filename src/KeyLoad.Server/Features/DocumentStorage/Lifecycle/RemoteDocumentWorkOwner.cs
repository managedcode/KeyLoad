using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

/// <summary>Retains runtime-private admission and original scopes; database work belongs to the enclosing shared native leases.</summary>
internal sealed class RemoteDocumentWorkOwner : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource stopping = new();
    private readonly ReplicaDiscoveryLifetime lifetime;
    private readonly SemaphoreSlim? admissions;
    private Task? disposal;
    private int joined;
    private const int NotJoined = 0;
    private const int Joined = 1;

    internal RemoteDocumentWorkOwner(IOptions<NodeOptions> nodeOptions, IOptions<RemoteDocumentExecutionOptions> options)
    {
        admissions = nodeOptions.Value.PhysicalGroupTrust.RemoteDocumentReads
            ? new(options.Value.MaximumAdmissions, options.Value.MaximumAdmissions) : null;
        lifetime = new(stopping, MarkJoined);
    }

    internal bool IsJoined => admissions is null || Volatile.Read(ref joined) == Joined;

    internal Operation Acquire(Guid requestId)
    {
        if (requestId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteDocumentProtocol.InvalidProof);
        }
        lock (gate)
        {
            if (admissions is null)
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteDocumentProtocol.Unavailable);
            }
            var original = lifetime.TryEnter()
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
            var admitted = false;
            try
            {
                admitted = admissions.Wait(TimeSpan.Zero);
                if (!admitted)
                {
                    throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteDocumentProtocol.Unavailable);
                }
                return new(ReleaseAdmission, original);
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                ThrowAcquisitionFailure(original, admitted, error);
                throw;
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                ThrowAcquisitionFailure(original, admitted, error);
                throw;
            }
        }
    }

    internal Task StopAsync() => lifetime.StopAsync();

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(StopAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(stopping.Dispose, failures);
        ServerFailureObserver.Observe(() => admissions?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void ThrowAcquisitionFailure(ReplicaDiscoveryLifetime.Operation original, bool admitted, Exception primary)
    {
        var failures = new List<Exception> { primary };
        if (admitted)
        {
            ServerFailureObserver.Observe(ReleaseAdmission, failures);
        }
        ServerFailureObserver.Observe(original.Dispose, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void MarkJoined() => Volatile.Write(ref joined, Joined);
    private void ReleaseAdmission() => admissions!.Release();

    internal sealed class Operation(Action releaseAdmission, ReplicaDiscoveryLifetime.Operation original) : IDisposable
    {
        private Action? release = releaseAdmission;
        internal CancellationToken ShutdownToken => original.ShutdownToken;

        public void Dispose()
        {
            var returnPermit = Interlocked.Exchange(ref release, null);
            if (returnPermit is null)
            {
                return;
            }
            var failures = new List<Exception>();
            ServerFailureObserver.Observe(returnPermit, failures);
            ServerFailureObserver.Observe(original.Dispose, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
