using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Shares one bounded, deduplicated resource lifetime ledger across an original request and its children.</summary>
internal sealed class ResourceLifetimeWorkResultRetention : IDisposable
{
    private const int Empty = 0;
    private const string Capacity = "The retained resource lifetimes exceed the operation result budget.";
    private const string Invalid = "The resource lifetime ledger input is invalid.";
    private readonly object gate = new();
    private readonly HashSet<ResourceLifetimeV1> retained = [];
    private readonly DatabaseLimits limits;
    private long retainedBytes;
    private bool disposed;

    internal ResourceLifetimeWorkResultRetention(IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        limits = options.Value;
        limits.Validate();
    }

    internal int RetainedEntries
    {
        get
        {
            lock (gate)
            {
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
                return retainedBytes;
            }
        }
    }

    internal void Reserve(ImmutableArray<ResourceLifetimeV1> lifetimes)
        => Reserve(lifetimes, limits.MaxBatchBytes);

    internal void Reserve(ImmutableArray<ResourceLifetimeV1> lifetimes, int maximumBytes)
    {
        if (lifetimes.IsDefault || maximumBytes <= Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, Invalid);
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var bound = Math.Min(maximumBytes, limits.MaxBatchBytes);
            var pending = new HashSet<ResourceLifetimeV1>();
            long additionalBytes = Empty;
            foreach (var lifetime in lifetimes)
            {
                ResourceLifetimeIdentity.Validate(lifetime);
                if (!retained.Contains(lifetime) && pending.Add(lifetime))
                {
                    additionalBytes += NativeSerialization.Measure(lifetime);
                }
                if (additionalBytes > bound - retainedBytes
                    || pending.Count > limits.MaxScanRecords - retained.Count)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Capacity);
                }
            }
            if (retainedBytes > bound)
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
            var available = Math.Min(maximumBytes, limits.MaxBatchBytes) - retainedBytes;
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
