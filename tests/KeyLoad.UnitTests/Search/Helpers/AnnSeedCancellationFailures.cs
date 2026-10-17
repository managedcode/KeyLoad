using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnSeedCancellationFailures
{
    private const string FailureMessage = "The seed capture and cancellation observer did not settle cleanly.";

    internal static void ThrowIfAny(Exception? primary, OperationCanceledException? canceled,
        List<Exception> settlement)
    {
        var failures = new List<Exception>();
        if (primary is not null)
        {
            failures.Add(primary);
        }
        if (settlement.Count > 0 && canceled is not null)
        {
            failures.Add(canceled);
        }
        foreach (var failure in settlement)
        {
            if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                failures.Add(failure);
            }
        }
        ThrowOrdered(failures);
    }

    private static void ThrowOrdered(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        var fatal = FindFatal(failures);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(fatal ?? failures[0]).Throw();
        }
        if (fatal is not null)
        {
            var fatalOwner = FindFatalOwner(failures);
            if (fatalOwner > 0)
            {
                (failures[0], failures[fatalOwner]) = (failures[fatalOwner], failures[0]);
            }
        }
        throw new AggregateException(FailureMessage, failures);
    }

    private static Exception? FindFatal(List<Exception> failures)
    {
        foreach (var failure in failures)
        {
            var fatal = CqrsRuntimeFailures.FindFatal(failure);
            if (fatal is not null)
            {
                return fatal;
            }
        }
        return null;
    }

    private static int FindFatalOwner(List<Exception> failures)
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
