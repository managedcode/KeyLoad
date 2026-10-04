namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildAdmission(int capacity, int maximumQueued, TimeSpan admissionDeadline)
{
    private readonly object _sync = new();
    private readonly LinkedList<Waiter> _pending = new();
    private readonly int _capacity = RequirePositive(capacity);
    private readonly int _maximumQueued = RequirePositive(maximumQueued);
    private readonly TimeSpan _admissionDeadline = RequirePositive(admissionDeadline);
    private int _active;
    private InvalidOperationException? _failure;

    internal static SiteHeavyChildAdmission Shared { get; } = new(SiteHeavyChildTokens.ActiveCapacity,
        SiteHeavyChildTokens.MaximumQueued, TimeSpan.FromMinutes(SiteHeavyChildTokens.AdmissionMinutes));

    internal int PendingCount
    {
        get
        {
            lock (_sync)
            {
                return _pending.Count;
            }
        }
    }

    internal async Task<SiteHeavyChildLease> AcquireAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var waiter = Enqueue(token);
        try
        {
            await waiter.Completion.Task.WaitAsync(_admissionDeadline, token);
            return new(this);
        }
        catch (Exception)
        {
            Withdraw(waiter);
            throw;
        }
    }

    private Waiter Enqueue(CancellationToken token)
    {
        lock (_sync)
        {
            if (_failure is not null)
            {
                throw new InvalidOperationException(SiteHeavyChildTokens.UnsafeOwnership, _failure);
            }
            token.ThrowIfCancellationRequested();
            var waiter = new Waiter(token);
            if (_active < _capacity && _pending.Count == SiteTokens.Zero)
            {
                Grant(waiter);
                return waiter;
            }
            if (_pending.Count >= _maximumQueued)
            {
                throw new InvalidOperationException(SiteHeavyChildTokens.QueueFull);
            }
            waiter.Node = _pending.AddLast(waiter);
            return waiter;
        }
    }

    private void Withdraw(Waiter waiter)
    {
        var releaseGrant = false;
        lock (_sync)
        {
            if (waiter.Node is { List: not null } node)
            {
                _pending.Remove(node);
                waiter.Node = null;
                _ = waiter.Completion.TrySetCanceled();
                return;
            }
            if (waiter.Completion.Task.IsCompletedSuccessfully)
            {
                releaseGrant = true;
            }
            if (waiter.Completion.Task.IsFaulted)
            {
                _ = waiter.Completion.Task.Exception;
            }
        }
        if (releaseGrant)
        {
            Finish(safelySettled: true);
        }
    }

    internal void Finish(bool safelySettled)
    {
        lock (_sync)
        {
            _active--;
            if (!safelySettled)
            {
                _failure = new InvalidOperationException(SiteHeavyChildTokens.UnsafeOwnership);
                RejectPending();
            }
            else if (_failure is null)
            {
                Drain();
            }
        }
    }

    private void RejectPending()
    {
        foreach (var waiter in _pending)
        {
            waiter.Node = null;
            _ = waiter.Completion.TrySetException(new InvalidOperationException(SiteHeavyChildTokens.UnsafeOwnership, _failure));
        }
        _pending.Clear();
    }

    private void Drain()
    {
        while (_active < _capacity && _pending.First is { } first)
        {
            _pending.RemoveFirst();
            var waiter = first.Value;
            waiter.Node = null;
            if (waiter.Token.IsCancellationRequested)
            {
                _ = waiter.Completion.TrySetCanceled(waiter.Token);
            }
            else
            {
                Grant(waiter);
            }
        }
    }

    private void Grant(Waiter waiter)
    {
        _active++;
        if (!waiter.Completion.TrySetResult())
        {
            _active--;
        }
    }

    private static int RequirePositive(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, SiteTokens.One);
        return value;
    }

    private static TimeSpan RequirePositive(TimeSpan value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
        return value;
    }

    private sealed class Waiter(CancellationToken token)
    {
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Waiter>? Node { get; set; }
    }
}
