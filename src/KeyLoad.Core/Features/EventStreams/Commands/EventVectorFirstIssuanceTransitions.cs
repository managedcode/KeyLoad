using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceTransitions
{
    private const string Invalid = "The original event vector first issuance transition requires recovery.";

    internal static EventVectorFirstIssuance Execute(IAtomicTransaction transaction,
        PrincipalRecord principal, EventVectorControlPhase phase, IAuthorizationPolicy authorization,
        IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(options);
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var original = EventVectorFirstIssuanceValidation.Read(transaction, request.ControlPartition,
            principal.Id, request.CommandId, terminal: false, admission);
        EventVectorParentScopes.Authorize(authorization, principal,
            original.OriginalAuthorizedSourceScopes, admission);
        EventVectorFirstIssuanceValidation.RequireRequest(original, request, admission);
        if (original.OriginalParentRequestId != phase.OriginalParentRequestId
            || original.OriginalParentExpiresAt != phase.OriginalParentExpiresAt
            || !original.OriginalCoverageWitnessBytes.Span.SequenceEqual(phase.OriginalCoverageWitness.Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        var first = EventVectorFirstPhaseCarrier.Read(phase.OriginalCapturedFirstOpenPhaseBytes, admission);
        EventVectorFirstPhaseCarrier.Require(phase, admission);
        EventVectorParentScopes.RequireExact(original.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(first, null, admission));
        var next = phase.Action switch
        {
            EventVectorControlAction.CaptureFirstPhase
                when original.State == EventVectorFirstIssuanceState.Admitted
                => original with
                {
                    State = EventVectorFirstIssuanceState.Captured,
                    OriginalCapturedFirstOpenPhaseBytes = phase.OriginalCapturedFirstOpenPhaseBytes.ToArray()
                },
            EventVectorControlAction.ReserveFirstDispatch
                when original.State == EventVectorFirstIssuanceState.Captured
                    && original.OriginalCapturedFirstOpenPhaseBytes.Span.SequenceEqual(
                        phase.OriginalCapturedFirstOpenPhaseBytes.Span)
                => original with { State = EventVectorFirstIssuanceState.DispatchReserved },
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid)
        };
        EventVectorFirstIssuanceWriter.Replace(transaction, original, next, admission);
        return next;
    }
}

