using System.Collections.Immutable;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Clustering;

/// <summary>Retains the original child admission until its actual dispatch and producer have joined.</summary>
internal sealed class NativeRequestWorkChildReservation(NativeRequestWorkOwner owner,
    NativeRequestWorkLease parent, NativeRequestWorkLease child, NativeRequestWorkChildIdentity identity)
{
    private readonly Lock gate = new();
    private bool consumed;
    private bool dispatchClosed;
    private Task<NativeRequestWorkChildDispatchSettlement>? completion;
    internal NativeRequestWorkLease Parent => parent;
    internal NativeRequestWorkLease Child => child;
    internal CancellationToken Cancellation => parent.Cancellation;
    internal ResourceLifetimeWorkResultRetention ResultRetention => child.ResultRetention;
    internal Task<NativeRequestWorkOriginalSettlement> Settlement => child.Settlement;
    internal NativeRequestWorkChildDispatchSettlement? TerminalReport
    {
        get; private set;
    }

    // Called only while the original work owner's admission gate is held.
    internal void Consume(NativeRequestWorkChildIdentity actual)
    {
        if (dispatchClosed || consumed || identity.RequestId != actual.RequestId
            || !identity.Fingerprint.AsSpan().SequenceEqual(actual.Fingerprint.AsSpan()))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
        consumed = true;
    }

    // Called only while the original work owner's admission gate is held.
    internal void RequireExecuting()
    {
        if (!consumed || dispatchClosed)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
        }
    }

    // The original owner gate serializes this decision with verified consumption.
    internal bool CloseDispatch()
    {
        dispatchClosed = true;
        return consumed;
    }

    internal Task<NativeRequestWorkChildDispatchSettlement> CompleteDispatchAsync(Exception? original,
        ImmutableArray<Exception> cleanup)
    {
        lock (gate)
        {
            return completion ??= CompleteAsync(original, cleanup);
        }
    }

    private async Task<NativeRequestWorkChildDispatchSettlement> CompleteAsync(Exception? original, ImmutableArray<Exception> cleanup)
    {
        await Task.Yield();
        var wasConsumed = owner.BeginChildDispatchCompletion(this);
        NativeRequestWorkOriginalSettlement? outcome = null;
        var joinFailure = await NativeRequestWorkSettlement.JoinAsync(JoinChildAsync()).ConfigureAwait(false);
        var report = new NativeRequestWorkChildDispatchSettlement(original, cleanup, outcome, joinFailure, null, null);
        TerminalReport = report;
        var recordFailure = NativeRequestWorkSettlement.Observe(() => owner.RecordChildCleanup(parent, report.CleanupFailures));
        if (recordFailure is not null)
        {
            report = report with
            {
                ParentRecordFailure = recordFailure
            };
            TerminalReport = report;
            NativeRequestWorkSettlement.Rethrow(report.CleanupOrFatalFailure);
            return report;
        }
        var releaseFailure = NativeRequestWorkSettlement.Observe(() => owner.ReleaseCloseObserver(parent));
        report = report with
        {
            ParentReleaseFailure = releaseFailure
        };
        TerminalReport = report;
        NativeRequestWorkSettlement.Rethrow(report.CleanupOrFatalFailure);
        return report;

        async Task JoinChildAsync()
        {
            outcome = wasConsumed ? await child.Settlement.ConfigureAwait(false)
                : await child.CompleteAsync(original, null).ConfigureAwait(false);
        }

    }

}

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
