namespace KeyLoad.ComparisonHost.Measurement;

/// <summary>Counts failed units across concurrent clients and keeps the first error.</summary>
internal sealed class FailureCounter
{
    private long units;
    private string? first;

    internal long Units => Interlocked.Read(ref units);

    internal string? First => Volatile.Read(ref first);

    internal void Add(Exception error, int count)
    {
        Interlocked.Add(ref units, count);
        Interlocked.CompareExchange(ref first, CaseResult.Describe(error), null);
    }

    internal void Add(string error, int count)
    {
        Interlocked.Add(ref units, count);
        Interlocked.CompareExchange(ref first, error, null);
    }
}
