using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedAggregateNodeFailureSet
{
    private readonly List<Exception> failures = [];

    internal void Add(Exception failure)
    {
        if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            failures.Add(failure);
        }
    }

    internal void Throw(Exception? primary)
    {
        ThrowExcepting(primary, []);
    }

    internal void ThrowExcepting(Exception? primary, params Exception[] expected)
    {
        var secondary = failures.Where(failure => !ReferenceEquals(primary, failure)
            && !expected.Contains(failure, ReferenceEqualityComparer.Instance)).ToArray();
        if (primary is not null && secondary.Length == 0)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (primary is null && secondary.Length == 1)
        {
            ExceptionDispatchInfo.Capture(secondary[0]).Throw();
        }
        if (primary is not null || secondary.Length > 0)
        {
            throw new AggregateException("Native Node operation and cleanup failed.",
                primary is null ? secondary : [primary, .. secondary]);
        }
    }
}
