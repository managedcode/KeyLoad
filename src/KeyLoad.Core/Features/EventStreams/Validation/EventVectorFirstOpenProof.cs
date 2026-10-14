using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstOpenProof
{
    private const string Invalid = "The retained original first Open proof requires recovery.";

    internal static (ReadOnlyMemory<byte> Bytes, Guid CommandId) RequireReserved(
        IKeyValueView view, PrincipalRecord principal, EventVectorControlPhase phase,
        ReplicatedOperation operation, ReadOnlyMemory<byte> actualRawPhase,
        IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        if (phase.Action != EventVectorControlAction.Open)
        {
            return (ReadOnlyMemory<byte>.Empty, Guid.Empty);
        }
        var request = phase.OriginalRequest;
        var issuance = EventVectorFirstIssuanceValidation.Read(view, request.ControlPartition,
            principal.Id, request.CommandId, terminal: false, admission);
        EventVectorParentScopes.Authorize(authorization, principal, issuance.OriginalAuthorizedSourceScopes, admission);
        _ = EventVectorFirstIssuanceValidation.RequireRequest(issuance, request, admission);
        if (issuance.State != EventVectorFirstIssuanceState.DispatchReserved
            || issuance.OriginalParentRequestId != phase.OriginalParentRequestId
            || issuance.OriginalParentExpiresAt != phase.OriginalParentExpiresAt
            || !phase.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || phase.OriginalFirstIssuancePublicCommandId != Guid.Empty
            || !actualRawPhase.Span.SequenceEqual(issuance.OriginalCapturedFirstOpenPhaseBytes.Span)
            || EventVectorControlPhaseIds.For(phase, principal.Id,
                EventVectorControlBodyProjection.Digest(phase, admission), admission) != operation.Id)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return (actualRawPhase, operation.Id);
    }

    internal static (ReadOnlyMemory<byte> Bytes, Guid CommandId) Preserve(
        EventVectorParentCall? previous, EventVectorControlPhase phase,
        (ReadOnlyMemory<byte> Bytes, Guid CommandId) actualFirst)
    {
        if (previous is null)
        {
            RequirePair(actualFirst.Bytes, actualFirst.CommandId);
            return actualFirst;
        }
        if (previous.OriginalPublicCommandId != phase.OriginalRequest.CommandId)
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
        RequirePair(previous.OriginalFirstOpenPhaseBytes, previous.OriginalFirstOpenNativeCommandId);
        if (!actualFirst.Bytes.IsEmpty
            && (!actualFirst.Bytes.Span.SequenceEqual(previous.OriginalFirstOpenPhaseBytes.Span)
                || actualFirst.CommandId != previous.OriginalFirstOpenNativeCommandId))
        {
            throw Errors.Fail(ErrorCode.Conflict, Invalid);
        }
        return (previous.OriginalFirstOpenPhaseBytes, previous.OriginalFirstOpenNativeCommandId);
    }

    internal static void RequirePair(ReadOnlyMemory<byte> bytes, Guid commandId)
    {
        if (bytes.IsEmpty != (commandId == Guid.Empty))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
    }
}
