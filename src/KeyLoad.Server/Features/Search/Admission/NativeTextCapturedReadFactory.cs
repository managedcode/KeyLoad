using System.Runtime.CompilerServices;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextCapturedReadFactory
{
    private const int NoFailures = 0;
    private const int NoImportedRecords = 0;

    private const string Invalid = "The native search snapshot does not match its original source authority.";

    internal static ICapturedTextRead Capture(DatabaseEngine database, ITextProjection provider, IKeyValueView raw,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, ReadExecutionBudget budget)
    {
        if (database.Store is not ZoneTreeStore owner)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, Invalid); }
        var authority = NativeSearchTerminalPrivacy.Capture(database, raw, principal, resource, request.Partition, budget);
        var projection = SelectedTextProjectionAdmission.Acquire(database, provider, raw, principal, resource, request, budget)
            ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, Invalid);
        ReadExecutionBudgetReadGrant? grant = null;
        ZoneTreeReadCutLease? native = null;
        ICapturedTextRead? result = null;
        var captureUncertain = false;
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(() =>
        {
            grant = budget.CreateReadGrant(budget.RemainingReadGrantBytes, budget.RemainingReadGrantRecords);
            AdmitMetadata(budget, grant, principal, resource, request, authority);
            try
            {
                native = owner.CaptureNativeReadCut(raw,
                    new(grant.RemainingRecords, grant.RemainingBytes, budget.RemainingLifetime), budget.Cancellation);
            }
            catch (AggregateException) { captureUncertain = true; throw; }
            RequireCut(native, authority);
            result = new NativeTextCapturedRead(database, request, budget, grant, authority, native,
                projection, principal, resource);
            budget.Check();
        }, failures);
        if (failures.Count != NoFailures)
        { SettleFailedCapture(budget, grant, native, projection, captureUncertain, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private static void AdmitMetadata(ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant,
        PrincipalRecord principal, ResourceDefinition resource, SearchRequest request, NativeSearchReadAuthority authority)
    {
        var bytes = checked(NativeSerialization.Measure(principal) + NativeSerialization.Measure(resource)
            + NativeSerialization.Measure(request) + NativeSerialization.Measure(authority.Placement)
            + Unsafe.SizeOf<NativeSearchReadAuthority>() + (long)authority.ResourceSha256.Length * sizeof(char));
        budget.ImportReadGrant(grant, bytes, NoImportedRecords);
    }

    private static void RequireCut(ZoneTreeReadCutLease native, NativeSearchReadAuthority original)
    {
        var cut = native.Cut;
        if (cut.NodeId != original.NodeId || cut.Incarnation != original.Incarnation
            || cut.FormatVersion != original.DataEpoch || cut.ReadGeneration != original.ReadGeneration
            || cut.Position != original.Position)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid); }
    }

    private static void SettleFailedCapture(ReadExecutionBudget budget, ReadExecutionBudgetReadGrant? grant,
        ZoneTreeReadCutLease? native, ITextProjectionLease projection, bool uncertain, List<Exception> failures)
    {
        var beforeNative = failures.Count;
        if (native is not null)
        { ServerFailureObserver.Observe(native.Dispose, failures); }
        if (!uncertain && failures.Count == beforeNative && grant is { IsCompleted: false })
        { ServerFailureObserver.Observe(() => budget.CompleteSettledReadGrant(grant), failures); }
        ServerFailureObserver.Observe(projection.Dispose, failures);
    }
}
