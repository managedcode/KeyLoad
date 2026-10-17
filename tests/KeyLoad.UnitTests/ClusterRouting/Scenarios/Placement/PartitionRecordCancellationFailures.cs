using System.Runtime.ExceptionServices;
using KeyLoad.Server;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.UnitTests.Features.ClusterRouting.Helpers;

internal static class PartitionRecordCancellationFailures
{
    internal static Exception? Single(List<Exception> failures)
        => failures.Count switch
        {
            0 => null,
            1 => failures[0],
            _ => new AggregateException(failures)
        };

    internal static void ThrowUnexpected(List<Exception> reader, List<Exception> observer,
        List<Exception> operation, List<Exception> cleanup, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        var hasSettlementFailure = observer.Count + operation.Count + cleanup.Count > 0;
        AddReaderFailures(reader, hasSettlementFailure, failures, cancellationToken);
        AddDistinct(observer, failures);
        AddDistinct(operation, failures);
        AddDistinct(cleanup, failures);
        if (failures.Count == 0)
        {
            return;
        }

        var fatal = CqrsRuntimeFailures.FindFatal(new AggregateException(failures));
        if (fatal is not null)
        {
            ExceptionDispatchInfo.Capture(fatal).Throw();
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void AddReaderFailures(List<Exception> reader, bool hasSettlementFailure,
        List<Exception> failures, CancellationToken token)
    {
        var expectedCancellation = reader.Count == 1
            && reader[0] is OperationCanceledException canceled
            && canceled.CancellationToken == token;
        if (!expectedCancellation || hasSettlementFailure)
        {
            AddDistinct(reader, failures);
        }
    }

    private static void AddDistinct(List<Exception> source, List<Exception> destination)
    {
        foreach (var failure in source)
        {
            if (!destination.Contains(failure, ReferenceEqualityComparer.Instance))
            {
                destination.Add(failure);
            }
        }
    }
}
