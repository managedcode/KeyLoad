using System.Collections.Immutable;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal sealed record NativeResourceWorkClosureReport(NativeResourceWorkClosureReceipt Receipt,
    ImmutableArray<Exception> CancellationFailures, ImmutableArray<Exception> ObserverReleaseFailures,
    ImmutableArray<Exception> JoinFailures)
{
    internal Exception? Failure
    {
        get
        {
            Exception? failure = null;
            foreach (var outcome in Receipt.OriginalOutcomes)
            {
                failure = NativeRequestWorkSettlement.Preserve(failure, CqrsRuntimeFailures.FindFatal(outcome.OriginalFailure));
                failure = NativeRequestWorkSettlement.Preserve(failure, outcome.CleanupOrJoinFailure);
            }
            foreach (var error in CancellationFailures.Concat(ObserverReleaseFailures).Concat(JoinFailures))
            {
                failure = NativeRequestWorkSettlement.Preserve(failure, error);
            }
            return failure;
        }
    }
}
