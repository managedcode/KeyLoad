using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentStreamOwnership
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<string, KurrentStreamOwnershipEntry> entries = new(StringComparer.Ordinal);

    internal KurrentStreamOwnership(IOptions<ComparisonOptions> workloadOptions)
        : this(Validated(workloadOptions))
    {
    }

    internal KurrentStreamOwnership(IComparisonSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Capacity = checked(options.Documents + options.Repetitions * options.Operations
            + KurrentConstants.OwnershipProbeCount);
    }

    internal int Capacity
    {
        get;
    }

    private static ComparisonOptions Validated(IOptions<ComparisonOptions> workloadOptions)
    {
        var options = workloadOptions?.Value ?? throw new ArgumentNullException(nameof(workloadOptions));
        options.Validate();
        return options;
    }
    internal int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    internal void Reserve(string stream, Guid runtimeEventId)
    {
        ArgumentException.ThrowIfNullOrEmpty(stream);
        if (runtimeEventId == Guid.Empty)
        {
            throw new ArgumentException(KurrentConstants.OwnershipInvalidDescriptor, nameof(runtimeEventId));
        }
        lock (gate)
        {
            if (entries.Count >= Capacity || !entries.TryAdd(stream, new(runtimeEventId, KurrentStreamOwnershipState.Reserved)))
            {
                throw new ComparisonFailureException(KurrentConstants.OwnershipReservationRejected);
            }
        }
    }

    internal void Acknowledge(string stream, Guid runtimeEventId)
        => Complete(stream, runtimeEventId, KurrentStreamOwnershipState.Acknowledged);

    internal void Reject(string stream, Guid runtimeEventId)
        => Complete(stream, runtimeEventId, KurrentStreamOwnershipState.Rejected);

    internal void MarkUnknown(string stream, Guid runtimeEventId)
        => Complete(stream, runtimeEventId, KurrentStreamOwnershipState.Unknown);

    internal string[] SnapshotAcknowledged()
    {
        lock (gate)
        {
            return entries.Where(pair => pair.Value.State == KurrentStreamOwnershipState.Acknowledged)
                .Select(pair => pair.Key).ToArray();
        }
    }

    private void Complete(string stream, Guid runtimeEventId, KurrentStreamOwnershipState state)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(stream, out var entry) || entry.RuntimeEventId != runtimeEventId ||
                entry.State != KurrentStreamOwnershipState.Reserved)
            {
                throw new ComparisonFailureException(KurrentConstants.OwnershipTransitionRejected);
            }
            entries[stream] = entry with
            {
                State = state
            };
        }
    }
}
