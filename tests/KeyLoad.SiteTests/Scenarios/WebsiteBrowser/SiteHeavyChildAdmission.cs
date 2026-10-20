namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteHeavyChildAdmission(Microsoft.Extensions.Options.IOptions<SiteHeavyChildOptions> options)
{
    private readonly System.Threading.Lock _sync = new();
    private readonly LinkedList<Waiter> _pending = new();
    private readonly SiteHeavyChildOptions _policy = options.Value;
    private int _active;
    private InvalidOperationException? _failure;

    internal static SiteHeavyChildAdmission Shared
    {
        get;
    } = new(SiteHeavyChildConfiguration.Bind(SiteExecutionConfiguration.Options.Value.HeavyChildCapacity,
        SiteExecutionConfiguration.Options.Value.MaximumQueued, SiteExecutionConfiguration.Options.Value.AdmissionDeadline));

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

    internal bool HasPendingWaiter(CancellationToken token)
    {
        lock (_sync)
        {
            foreach (var waiter in _pending)
            {
                if (waiter.Token.Equals(token))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal async Task<SiteHeavyChildLease> AcquireAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var waiter = Enqueue(token);
        try
        {
            await waiter.Completion.Task.WaitAsync(_policy.AdmissionDeadline, TimeProvider.System, token);
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
            if (_active < _policy.Capacity && _pending.Count == SiteTokens.Zero)
            {
                Grant(waiter);
                return waiter;
            }
            if (_pending.Count >= _policy.MaximumQueued)
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
        while (_active < _policy.Capacity && _pending.First is { } first)
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

    private sealed class Waiter(CancellationToken token)
    {
        internal CancellationToken Token { get; } = token;
        internal TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal LinkedListNode<Waiter>? Node
        {
            get; set;
        }
    }
}
