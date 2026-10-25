using System.Collections.Immutable;

namespace KeyLoad.Core.ResourceExecution;

/// <summary>Shares one deduplicated resource lifetime ledger across an original request and its children.</summary>
internal sealed class ResourceLifetimeWorkResultRetention : IDisposable
{
    private const int Empty = 0;
    private const string Capacity = "The retained resource lifetimes exceed the operation result bytes.";
    private const int Unbounded = int.MaxValue;
    private const string Invalid = "The resource lifetime ledger input is invalid.";
    private readonly Lock gate = new();
    private readonly HashSet<ResourceLifetime> retained = [];
    private long retainedBytes;
    private bool disposed;

    internal int RetainedEntries
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return retained.Count;
            }
        }
    }

    internal long RetainedEncodedBytes
    {
        get
        {
            lock (gate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return retainedBytes;
            }
        }
    }

    internal void Reserve(ImmutableArray<ResourceLifetime> lifetimes)
        => Reserve(lifetimes, Unbounded);

    internal void Reserve(ImmutableArray<ResourceLifetime> lifetimes, int maximumBytes)
    {
        if (lifetimes.IsDefault || maximumBytes <= Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var pending = new HashSet<ResourceLifetime>();
            long additionalBytes = Empty;
            foreach (var lifetime in lifetimes)
            {
                ResourceLifetimeIdentity.Validate(lifetime);
                if (!retained.Contains(lifetime) && pending.Add(lifetime))
                {
                    additionalBytes += OrleansSerialization.Measure(lifetime);
                }
                if (additionalBytes > maximumBytes - retainedBytes)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                }
            }
            if (retainedBytes > maximumBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
            }
            retained.UnionWith(pending);
            retainedBytes += additionalBytes;
        }
    }

    internal int AvailableResultBytes(int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var available = maximumBytes - retainedBytes;
            if (available <= Empty)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
            }
            return checked((int)available);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            retained.Clear();
            retainedBytes = Empty;
        }
    }
}
