using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class ScaleServerProbeFailureOrder
{
    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        var fatal = failures.Select(CqrsRuntimeFailures.FindFatal).FirstOrDefault(item => item is not null);
        var ordered = new List<Exception>();
        if (fatal is not null)
        {
            ordered.Add(fatal);
        }

        foreach (var failure in failures)
        {
            if (!ordered.Any(existing => ReferenceEquals(existing, failure)))
            {
                ordered.Add(failure);
            }
        }
        if (ordered.Count == 1)
        {
            ExceptionDispatchInfo.Capture(ordered[0]).Throw();
        }

        throw new AggregateException(ordered);
    }
}
