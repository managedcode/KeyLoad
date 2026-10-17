using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains original worker and cancellation callback errors until every worker settles.</summary>
internal sealed class IsolatedKurrentVolumeWorkerFailures
{
    private const string JoinedFailures = "Kurrent volume workers and native cancellation callbacks failed.";
    private readonly Lock gate = new();
    private readonly HashSet<Exception> seen = new(ReferenceEqualityComparer.Instance);
    private readonly List<Exception> originals = [];

    internal void Capture(Task original, Exception observed)
    {
        lock (gate)
        {
            Record(observed);
            if (original.Exception is { } aggregate)
            {
                foreach (var failure in aggregate.InnerExceptions)
                {
                    Record(failure);
                }
            }
        }
    }

    internal void ThrowAfterWorkers()
    {
        Exception[] retained;
        lock (gate)
        {
            retained = [.. originals];
        }
        if (retained.Length == 1)
        {
            ExceptionDispatchInfo.Capture(retained[0]).Throw();
        }
        if (retained.Length > 1)
        {
            throw new AggregateException(JoinedFailures, retained);
        }
    }

    private void Record(Exception failure)
    {
        if (seen.Add(failure))
        {
            originals.Add(failure);
        }
    }
}
