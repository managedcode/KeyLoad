using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Retains one admitted immutable source image until every borrowed page has settled.</summary>
internal sealed class PartitionMovementImageSession : IAsyncDisposable
{
    private const int SingleBorrower = 1;
    private const int NoBorrowers = 0;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ICacheMemoryReservation reservation;
    private readonly CancellationTokenSource stopping = new();
    private PartitionMoveImage? image;
    private int readers;
    private bool closing;
    private Task? shutdown;

    internal PartitionMovementImageSession(PartitionMoveImage image, ICacheMemoryReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reservation);
        this.image = image;
        this.reservation = reservation;
    }

    internal PartitionMovementImagePageLease Borrow(int ordinal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            var current = image ?? throw new InvalidOperationException();
            if ((uint)ordinal >= (uint)current.Pages.Length)
            { throw new ArgumentOutOfRangeException(nameof(ordinal)); }
            var lease = new PartitionMovementImagePageLease(this, current.Pages[ordinal]);
            readers = checked(readers + SingleBorrower);
            return lease;
        }
    }

    internal CancellationToken StageCancellation => stopping.Token;

    internal void RequireOpen()
    {
        lock (gate)
        { ObjectDisposedException.ThrowIf(closing || image is null, this); }
    }

    internal void Return()
    {
        lock (gate)
        {
            readers--;
            if (closing && readers == NoBorrowers)
            { drained.TrySetResult(); }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            closing = true;
            if (readers == NoBorrowers)
            { drained.TrySetResult(); }
            shutdown ??= ReleaseAsync();
            return new(shutdown);
        }
    }

    private async Task ReleaseAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(stopping.CancelAsync, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => drained.Task, failures).ConfigureAwait(false);
        lock (gate)
        { image = null; }
        ServerFailureObserver.Observe(reservation.Dispose, failures);
        try
        { stopping.Dispose(); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
