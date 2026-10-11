using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstReleaseAdmission
{
    private const string Invalid = "The original event vector first issuance release requires recovery.";

    internal static EventVectorFirstIssuance Require(IKeyValueView view, PrincipalRecord principal,
        EventVectorControlPhase phase, bool originalOutcome, IAuthorizationPolicy authorization,
        EventVectorAdmissionPolicy admission)
    {
        var request = phase.OriginalRequest;
        var target = request.OriginalFirstIssuancePublicCommandId;
        var active = EventVectorRowStorage.Read<EventVectorFirstIssuance>(view,
            EventVectorKeys.FirstIssuance(request.ControlPartition, principal.Id, target), admission);
        var terminal = EventVectorRowStorage.Read<EventVectorFirstIssuance>(view,
            EventVectorKeys.TerminalFirstIssuance(request.ControlPartition, principal.Id, target), admission);
        if (active is not null && terminal is not null || !originalOutcome && terminal is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var row = EventVectorFirstIssuanceValidation.Read(view, request.ControlPartition, principal.Id,
            target, terminal: active is null, admission);
        EventVectorParentScopes.Authorize(authorization, principal, row.OriginalAuthorizedSourceScopes, admission);
        admission.RequireEncodedBytes(row.OriginalPublicRequestBytes.Length);
        var original = NativeSerialization.Deserialize<EventFeedControlRequest>(row.OriginalPublicRequestBytes.Span);
        EventVectorFirstIssuanceValidation.RequireRequest(row, original, admission);
        if (original.Scope != request.Scope || row.MapId != request.MapId
            || !row.OriginalCoverageWitnessBytes.Span.SequenceEqual(phase.OriginalCoverageWitness.Span)
            || !row.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(
                phase.OriginalCapturedFirstOpenPhaseBytes.Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        EventVectorParentScopes.RequireExact(row.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(phase, null, admission));
        EventVectorFirstPhaseCarrier.Require(phase, admission);
        if (originalOutcome)
        { return row; }
        var matches = phase.Action switch
        {
            EventVectorControlAction.AbandonFirstIssuance => row.State is EventVectorFirstIssuanceState.Admitted
                or EventVectorFirstIssuanceState.Captured or EventVectorFirstIssuanceState.DispatchReserved,
            EventVectorControlAction.CleanupAbandonedIssuance => row.State == EventVectorFirstIssuanceState.Abandoned,
            _ => false
        };
        if (!matches)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return row;
    }
}
