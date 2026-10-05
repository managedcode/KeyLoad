using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.RecoveryTests.Features.DocumentStorage;

internal static class CommandIdempotencyProcessFailureHandling
{
    private const string MultipleFailuresMessage = "The document command child and its output readers did not settle cleanly.";

    internal static bool IsNonFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is null;

    internal static Exception? PreserveStartupFailure(Exception? primary, Exception? additional)
    {
        if (primary is null || ReferenceEquals(primary, additional))
        {
            return additional ?? primary;
        }
        if (additional is null)
        {
            return primary;
        }
        var primaryFatal = CqrsRuntimeFailures.FindFatal(primary) is not null;
        var additionalFatal = CqrsRuntimeFailures.FindFatal(additional) is not null;
        return additionalFatal && !primaryFatal
            ? new AggregateException(additional, primary)
            : new AggregateException(primary, additional);
    }

    internal static void AddDistinct(List<Exception> failures, Exception failure)
    {
        if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            failures.Add(failure);
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(MultipleFailuresMessage, OrderFatalFirst(failures));
        }
    }

    private static List<Exception> OrderFatalFirst(List<Exception> failures)
    {
        var fatalIndex = FindFatalIndex(failures);
        if (fatalIndex <= 0)
        { return failures; }
        var ordered = new List<Exception>(failures.Count) { failures[fatalIndex] };
        for (var index = 0; index < failures.Count; index++)
        {
            if (index != fatalIndex)
            { ordered.Add(failures[index]); }
        }
        return ordered;
    }

    private static int FindFatalIndex(List<Exception> failures)
    {
        for (var index = 0; index < failures.Count; index++)
        {
            if (CqrsRuntimeFailures.FindFatal(failures[index]) is not null)
            {
                return index;
            }
        }
        return -1;
    }
}
