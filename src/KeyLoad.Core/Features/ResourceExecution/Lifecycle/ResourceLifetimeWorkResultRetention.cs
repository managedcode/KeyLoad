using System.Collections.Immutable;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Shares bounded encoded lifetime retention across one original operation and its local children.</summary>
internal sealed class ResourceLifetimeWorkResultRetention : IDisposable
{
    private const long NoAvailableBytes = 0;
    private const string Exceeded = "The read result byte budget is exceeded.";
    private const string MissingLifetimeSet = "The actual resource lifetime set is missing.";
    private readonly Lock gate = new();
    private readonly HashSet<ResourceLifetimeV1> retained = [];
    private readonly int maximumBytes;
    private readonly int maximumEntries;
    private long retainedBytes;
    private bool disposed;

    internal ResourceLifetimeWorkResultRetention(IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        limits.Validate();
        maximumBytes = limits.MaxBatchBytes;
        maximumEntries = limits.MaxScanRecords;
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

    internal int AvailableResultBytes(int operationMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(operationMaximumBytes);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return checked((int)Math.Max(0, Math.Min(maximumBytes, operationMaximumBytes) - retainedBytes));
        }
    }

    internal void Reserve(ImmutableArray<ResourceLifetimeV1> actualLifetimes)
        => Reserve(actualLifetimes, maximumBytes);

    internal void Reserve(ImmutableArray<ResourceLifetimeV1> actualLifetimes, int operationMaximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(operationMaximumBytes);
        if (actualLifetimes.IsDefault)
        {
            throw new ArgumentException(MissingLifetimeSet, nameof(actualLifetimes));
        }
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var admitted = new HashSet<ResourceLifetimeV1>();
            var addedBytes = 0L;
            var available = Math.Min(maximumBytes, operationMaximumBytes) - retainedBytes;
            if (available < NoAvailableBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
            }
            foreach (var lifetime in actualLifetimes)
            {
                ArgumentNullException.ThrowIfNull(lifetime);
                if (retained.Contains(lifetime) || admitted.Contains(lifetime))
                {
                    continue;
                }
                var bytes = NativeSerialization.Measure(lifetime);
                if (bytes > available - addedBytes || retained.Count + admitted.Count >= maximumEntries)
                {
                    throw Errors.Fail(ErrorCode.BudgetExceeded, Exceeded);
                }
                addedBytes = checked(addedBytes + bytes);
                admitted.Add(lifetime);
            }
            retained.UnionWith(admitted);
            retainedBytes = checked(retainedBytes + addedBytes);
        }
    }

    // Only the actual original joined owner disposes this ledger; local children merely borrow it.
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            retained.Clear();
            retainedBytes = 0;
        }
    }
}
