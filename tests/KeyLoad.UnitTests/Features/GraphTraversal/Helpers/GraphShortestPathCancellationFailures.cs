using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphShortestPathCancellationFailures
{
    private const string FailureMessage = "The shortest-path operation and cancellation observer did not settle.";

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
        if (failures.Count == 0)
        {
            return;
        }
        var fatalIndex = failures.FindIndex(failure => CqrsRuntimeFailures.FindFatal(failure) is not null);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(CqrsRuntimeFailures.FindFatal(failures[0]) ?? failures[0]).Throw();
        }
        if (fatalIndex > 0)
        {
            (failures[0], failures[fatalIndex]) = (failures[fatalIndex], failures[0]);
        }
        throw new AggregateException(FailureMessage, failures);
    }
}
