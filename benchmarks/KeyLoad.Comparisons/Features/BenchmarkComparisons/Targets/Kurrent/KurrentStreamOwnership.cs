using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentStreamOwnership
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<string, KurrentStreamOwnershipEntry> entries = new(StringComparer.Ordinal);

    internal KurrentStreamOwnership(IOptions<ComparisonOptions> workloadOptions)
    {
        ArgumentNullException.ThrowIfNull(workloadOptions);
        var options = workloadOptions.Value;
        options.Validate();
        Capacity = checked(options.Documents + options.Repetitions * checked(options.Warmup + options.Operations)
            + KurrentConstants.OwnershipProbeCount);
    }

    internal int Capacity { get; }
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

    internal void Reserve(string stream, Guid nativeEventId)
    {
        ArgumentException.ThrowIfNullOrEmpty(stream);
        if (nativeEventId == Guid.Empty)
        {
            throw new ArgumentException(KurrentConstants.OwnershipInvalidDescriptor, nameof(nativeEventId));
        }
        lock (gate)
        {
            if (entries.Count >= Capacity || !entries.TryAdd(stream, new(nativeEventId, KurrentStreamOwnershipState.Reserved)))
            {
                throw new ComparisonFailureException(KurrentConstants.OwnershipReservationRejected);
            }
        }
    }

    internal void Acknowledge(string stream, Guid nativeEventId)
        => Complete(stream, nativeEventId, KurrentStreamOwnershipState.Acknowledged);

    internal void Reject(string stream, Guid nativeEventId)
        => Complete(stream, nativeEventId, KurrentStreamOwnershipState.Rejected);

    internal void MarkUnknown(string stream, Guid nativeEventId)
        => Complete(stream, nativeEventId, KurrentStreamOwnershipState.Unknown);

    internal string[] SnapshotAcknowledged()
    {
        lock (gate)
        {
            return entries.Where(pair => pair.Value.State == KurrentStreamOwnershipState.Acknowledged)
                .Select(pair => pair.Key).ToArray();
        }
    }

    private void Complete(string stream, Guid nativeEventId, KurrentStreamOwnershipState state)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(stream, out var entry) || entry.NativeEventId != nativeEventId ||
                entry.State != KurrentStreamOwnershipState.Reserved)
            {
                throw new ComparisonFailureException(KurrentConstants.OwnershipTransitionRejected);
            }
            entries[stream] = entry with { State = state };
        }
    }
}
