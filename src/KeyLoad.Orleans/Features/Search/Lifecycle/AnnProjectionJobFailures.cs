using System.Runtime.ExceptionServices;

namespace KeyLoad.Orleans;

internal static class AnnProjectionJobFailures
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;

    internal static void ThrowIfAny(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == SingleFailure)
        { ExceptionDispatchInfo.Capture(failures[FirstFailure]).Throw(); }
        if (failures.Count > SingleFailure)
        { throw new AggregateException(failures); }
    }
}
