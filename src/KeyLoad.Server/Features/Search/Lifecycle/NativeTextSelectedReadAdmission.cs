namespace KeyLoad.Server.Features.Search;

/// <summary>Orders native reader ownership before maintenance canonical-view admission.</summary>
internal sealed class NativeTextSelectedReadAdmission(int maximumReaders, NativeTextResourceOwnership? resources = null)
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private int readers;
    private bool writer;
    private bool closed;
    private TaskCompletionSource? quiet;
    private Exception? retainedFailure;

    internal NativeTextSelectedReadLease EnterRead()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (retainedFailure is not null)
            { throw new AggregateException(retainedFailure); }
            if (writer || readers >= maximumReaders)
            { throw NativeTextErrors.BoundExceeded(); }
            var reservation = resources?.ReserveLease();
            if (readers == Empty)
            { quiet = new(TaskCreationOptions.RunContinuationsAsynchronously); }
            readers++;
            return new(this, reservation);
        }
    }

    internal async Task WaitForWriteAsync(CancellationToken token)
    {
        Task originalReaders;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (retainedFailure is not null)
            { throw new AggregateException(retainedFailure); }
            if (writer)
            { throw NativeTextErrors.Ownership(); }
            writer = true;
            originalReaders = quiet?.Task ?? Task.CompletedTask;
        }
        try
        {
            await originalReaders.WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(ExitWrite, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    internal void RetainFailure(Exception actual)
    {
        lock (gate)
        {
            retainedFailure = retainedFailure is null ? actual : new AggregateException(retainedFailure, actual);
        }
    }

    internal void ThrowRetained()
    {
        lock (gate)
        {
            if (retainedFailure is not null)
            { throw new AggregateException(retainedFailure); }
        }
    }

    internal Task CloseAndJoinReaders()
    {
        lock (gate)
        {
            closed = true;
            return quiet?.Task ?? Task.CompletedTask;
        }
    }

    internal void ExitRead(NativeTextResourceReservation? reservation)
    {
        lock (gate)
        {
            if (readers <= Empty)
            { throw NativeTextErrors.Corrupt(); }
            readers--;
            if (retainedFailure is null)
            { reservation?.CompleteAfterJoinedCleanup(); }
            if (readers == Empty)
            { quiet!.TrySetResult(); }
        }
    }

    internal void ExitWrite()
    {
        lock (gate)
        {
            if (!writer)
            { throw NativeTextErrors.Corrupt(); }
            writer = false;
        }
    }
}
