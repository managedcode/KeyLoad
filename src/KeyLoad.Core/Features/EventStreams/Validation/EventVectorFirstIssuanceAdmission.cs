using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceAdmission
{
    private const string Invalid = "The original event vector first issuance admission requires recovery.";

    internal static void Require(IKeyValueView view, PrincipalRecord principal,
        EventVectorControlPhase phase, bool originalOutcome, Guid incarnation, IAuthorizationPolicy authorization,
        EventVectorAdmissionPolicy admission)
    {
        if (phase.Action is EventVectorControlAction.AbandonFirstIssuance
            or EventVectorControlAction.CleanupAbandonedIssuance)
        {
            _ = EventVectorFirstReleaseAdmission.Require(view, principal, phase,
                originalOutcome, authorization, admission);
            return;
        }
        RequireOriginal(view, principal, phase, originalOutcome, incarnation, authorization, admission);
    }

    private static void RequireOriginal(IKeyValueView view, PrincipalRecord principal,
        EventVectorControlPhase phase, bool originalOutcome, Guid incarnation, IAuthorizationPolicy authorization,
        EventVectorAdmissionPolicy admission)
    {
        var request = phase.OriginalRequest;
        var activeKey = EventVectorKeys.FirstIssuance(request.ControlPartition,
            principal.Id, request.CommandId);
        var terminalKey = EventVectorKeys.TerminalFirstIssuance(request.ControlPartition,
            principal.Id, request.CommandId);
        var active = EventVectorRowStorage.Read<EventVectorFirstIssuance>(view, activeKey, admission);
        var terminal = EventVectorRowStorage.Read<EventVectorFirstIssuance>(view, terminalKey, admission);
        if (active is not null && terminal is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        if (phase.Action == EventVectorControlAction.AdmitFirstIntent && !originalOutcome)
        {
            if (active is not null || terminal is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            return;
        }
        if (phase.Action == EventVectorControlAction.AdmitFirstIntent && originalOutcome)
        {
            var admitted = EventVectorFirstAdmitOutcome.Read(view, principal, request,
                incarnation, authorization, admission);
            if (admitted.Outcome.Result.Error is not null)
            {
                if (active is not null || terminal is not null)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
                return;
            }
        }
        if (active is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var row = EventVectorFirstIssuanceValidation.Read(view, request.ControlPartition,
            principal.Id, request.CommandId, terminal: false, admission);
        EventVectorParentScopes.Authorize(authorization, principal, row.OriginalAuthorizedSourceScopes, admission);
        _ = EventVectorFirstIssuanceValidation.RequireRequest(row, request, admission);
        if (row.OriginalParentExpiresAt != phase.OriginalParentExpiresAt
            || !row.OriginalCoverageWitnessBytes.Span.SequenceEqual(phase.OriginalCoverageWitness.Span)
            || !CryptographicOperations.FixedTimeEquals(row.OriginalPublicRequestDigest.Span,
                EventVectorControlBodyProjection.RequestDigest(request, admission).Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        EventVectorParentScopes.RequireExact(row.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(phase, null, admission));
        if (originalOutcome)
        { return; }
        var stateMatches = phase.Action switch
        {
            EventVectorControlAction.CaptureFirstPhase => row.State == EventVectorFirstIssuanceState.Admitted,
            EventVectorControlAction.ReserveFirstDispatch => row.State == EventVectorFirstIssuanceState.Captured,
            _ => false
        };
        if (!stateMatches)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
    }
}

