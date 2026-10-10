using KeyLoad.Orleans;
using ZoneTree.AbstractFileStream;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextBoundedStreamWrites(IFileStream original, NativeTextResourceOwnership resources, NativeTextOpenFileGroup group)
{
    private const int NoFailures = 0;
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    private readonly Lock gate = new();
    private TaskCompletionSource? active;
    private Exception? retained;
    private bool closing;

    internal NativeTextResourceFileReservation Begin(Func<long> targetLength)
    {
        Enter();
        var failures = new List<Exception>();
        NativeTextResourceFileReservation? reservation = null;
        ServerFailureObserver.Observe(() =>
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (retained is not null)
            { throw new AggregateException(retained); }
            reservation = resources.ReserveFile(original.FilePath, targetLength(), () => original.Length, group);
        }, failures);
        if (failures.Count != NoFailures)
        {
            ServerFailureObserver.Observe(Exit, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return reservation!;
    }

    internal T MutatePosition<T>(Func<T> mutation)
    {
        Enter();
        var failures = new List<Exception>();
        T result = default!;
        ServerFailureObserver.Observe(() =>
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (retained is not null)
            { throw new AggregateException(retained); }
            result = mutation();
        }, failures);
        ServerFailureObserver.Observe(Exit, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    internal NativeTextResourceFileReservation BeginWrite(int count)
        => Begin(() => checked(Math.Max(original.Length, original.Position + count)));

    internal void Complete(NativeTextResourceFileReservation reservation, List<Exception> failures)
    {
        var beforeSettlement = failures.Count;
        ServerFailureObserver.Observe(reservation.CompleteAfterJoinedWrite, failures);
        if (failures.Count != beforeSettlement)
        { retained = failures.Count == SingleFailure ? failures[FirstFailure] : new AggregateException(failures); }
        ServerFailureObserver.Observe(Exit, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var reservation = BeginWrite(bytes.Length);
        var failures = new List<Exception>();
        try
        { await original.WriteAsync(bytes, token).ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error)) { failures.Add(error); }
        Complete(reservation, failures);
    }

    internal void ThrowRetained()
    {
        if (retained is not null)
        { ServerFailureObserver.ThrowIfAny([retained]); }
    }

    private void Enter()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closing, this);
            if (active is not null)
            { throw NativeTextErrors.Busy(); }
            active = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private void Exit()
    {
        TaskCompletionSource completed;
        lock (gate)
        {
            completed = active ?? throw NativeTextErrors.Ownership();
            active = null;
        }
        completed.SetResult();
    }

    internal void JoinForDisposal() => JoinForDisposalAsync().GetAwaiter().GetResult();

    internal Task JoinForDisposalAsync()
    {
        lock (gate)
        {
            closing = true;
            return active?.Task ?? Task.CompletedTask;
        }
    }
}
