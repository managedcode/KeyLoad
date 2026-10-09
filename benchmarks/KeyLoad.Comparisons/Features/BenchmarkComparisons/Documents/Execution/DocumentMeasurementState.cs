namespace KeyLoad.Comparisons;

internal sealed class DocumentMeasurementState(int planned)
{
    private int active, peak;
    private long attempts, acknowledged, failed, canceled;
    private readonly HashSet<string> errorCategories = [];
    private Exception? fatal;
    private readonly System.Threading.Lock gate = new();
    internal int Planned => planned;
    internal int Active => Volatile.Read(ref active);
    internal int Peak => peak;
    internal long Attempts => attempts;
    internal long Acknowledged => acknowledged;
    internal long Failed => failed;
    internal long Canceled => canceled;
    internal DocumentLatencyRecorder Latency { get; } = new();
    internal Exception? Fatal { get { lock (gate) { return fatal; } } }
    internal void RecordFailure(Exception error) { lock (gate) { fatal ??= ManagedCode.Communication.CQRS.CqrsRuntimeFailures.FindFatal(error); if (errorCategories.Count < DocumentMeasurementValues.MaximumFailureCategories) { errorCategories.Add(ComparisonErrors.Safe(error)); } else { errorCategories.Add(DocumentProtocolText.DocumentAdditionalFailureCategories); } } }
    internal string[] FailureCategories()
    {
        lock (gate)
        {
            return errorCategories.Order(StringComparer.Ordinal).ToArray();
        }
    }
    internal void Begin()
    {
        Interlocked.Increment(ref attempts);
        var current = Interlocked.Increment(ref active);
        int observed;
        do
        {
            observed = Volatile.Read(ref peak);
            if (observed >= current)
            {
                break;
            }
        }
        while (Interlocked.CompareExchange(ref peak, current, observed) != observed);
    }
    internal void End(double latency, bool success, bool wasCanceled)
    {
        Latency.Record(latency);
        if (success)
        {
            Interlocked.Increment(ref acknowledged);
        }
        else if (wasCanceled)
        {
            Interlocked.Increment(ref canceled);
        }
        else
        {
            Interlocked.Increment(ref failed);
        }

        Interlocked.Decrement(ref active);
    }
}
