using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class PartitionQueryCancellationFailures
{
    private const string FailureMessage = "The partition query and native cancellation observer did not settle cleanly.";

    internal static void ThrowIfAny(Exception? primary, List<Exception> settlement)
    {
        var failures = new List<Exception>();
        if (primary is not null)
        {
            failures.Add(primary);
        }
        foreach (var failure in settlement)
        {
            if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                failures.Add(failure);
            }
        }
        ThrowWithFatalPriority(failures);
    }

    private static void ThrowWithFatalPriority(List<Exception> failures)
    {
        if (failures.Count == 0)
        {
            return;
        }
        var fatalIndex = failures.FindIndex(failure => CqrsRuntimeFailures.FindFatal(failure) is not null);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (fatalIndex > 0)
        {
            (failures[0], failures[fatalIndex]) = (failures[fatalIndex], failures[0]);
        }
        throw new AggregateException(FailureMessage, failures);
    }
}
