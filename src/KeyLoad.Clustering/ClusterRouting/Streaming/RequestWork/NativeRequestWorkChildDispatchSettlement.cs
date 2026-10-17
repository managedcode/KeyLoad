using System.Collections.Immutable;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

internal sealed record NativeRequestWorkChildDispatchSettlement(Exception? OriginalFailure,
    ImmutableArray<Exception> DispatchCleanupFailures, NativeRequestWorkOriginalSettlement? ChildOutcome,
    Exception? ChildJoinFailure, Exception? ParentRecordFailure, Exception? ParentReleaseFailure)
{
    internal ImmutableArray<Exception> CleanupFailures
    {
        get
        {
            var result = ImmutableArray.CreateBuilder<Exception>();
            result.AddRange(DispatchCleanupFailures);
            if (ChildOutcome is not null)
            {
                result.AddRange(ChildOutcome.CleanupFailures);
            }
            if (ChildJoinFailure is not null)
            {
                result.Add(ChildJoinFailure);
            }
            if (ParentRecordFailure is not null)
            {
                result.Add(ParentRecordFailure);
            }
            if (ParentReleaseFailure is not null)
            {
                result.Add(ParentReleaseFailure);
            }
            return result.ToImmutable();
        }
    }

    internal Exception? CleanupOrFatalFailure
    {
        get
        {
            var failure = NativeRequestWorkSettlement.Preserve(DispatchCleanupFailures.IsEmpty ? null
                : new AggregateException(DispatchCleanupFailures), ChildOutcome?.CleanupOrJoinFailure);
            failure = NativeRequestWorkSettlement.Preserve(failure, ChildJoinFailure);
            failure = NativeRequestWorkSettlement.Preserve(failure, ParentRecordFailure);
            failure = NativeRequestWorkSettlement.Preserve(failure, ParentReleaseFailure);
            var fatal = CqrsRuntimeFailures.FindFatal(OriginalFailure)
                        ?? CqrsRuntimeFailures.FindFatal(ChildOutcome?.OriginalFailure);
            return NativeRequestWorkSettlement.Preserve(fatal, failure);
        }
    }
}
