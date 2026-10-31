using KeyLoad.Core;
using KeyLoad.Core.Search;
using KeyLoad.Query.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Search;

internal static class TextCapturedReadFactory
{
    private const int NoFailures = 0;

    private const string Invalid = "The execution search snapshot does not match its original source authority.";

    internal static ICapturedTextRead Capture(DatabaseEngine database, ITextProjection provider, IKeyValueView raw,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        if (database.Store is not ZoneTreeStore owner)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid);
        }
        var authority = SearchTerminalPrivacy.Capture(database, raw, principal, resource, request.Partition, budget);
        var projection = SelectedTextProjectionAdmission.Acquire(database, provider, raw, principal, resource, request, budget)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Invalid);
        ZoneTreeReadCutLease? execution = null;
        ICapturedTextRead? result = null;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            execution = owner.CaptureReadCut(raw,
                owner.BoundReadCut(int.MaxValue, long.MaxValue, budget.RemainingLifetime),
                budget.Cancellation);
            RequireCut(execution, authority);
            result = new TextCapturedRead(database, request, budget, authority, execution,
                projection, principal, resource);
            budget.Check();
        }, failures);
        if (failures.Count != NoFailures)
        {
            SettleFailedCapture(execution, projection, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private static void RequireCut(ZoneTreeReadCutLease execution, SearchReadAuthority original)
    {
        var cut = execution.Cut;
        if (cut.NodeId != original.NodeId || cut.Incarnation != original.Incarnation
            || cut.FormatVersion != original.DataEpoch || cut.ReadGeneration != original.ReadGeneration
            || cut.Position != original.Position)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
    }

    private static void SettleFailedCapture(ZoneTreeReadCutLease? execution, ITextProjectionLease projection,
        List<Exception> failures)
    {
        if (execution is not null)
        {
            ServerFailureObserver.Observe(execution.Dispose, failures);
        }
        ServerFailureObserver.Observe(projection.Dispose, failures);
    }
}
