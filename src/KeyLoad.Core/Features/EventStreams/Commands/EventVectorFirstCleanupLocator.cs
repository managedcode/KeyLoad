using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstCleanupLocator
{
    private const int Version = 1;
    private const string Invalid = "The original first issuance cleanup outcome requires recovery.";

    internal static EventVectorParentOutcomeIndex Create(PrincipalRecord principal,
        EventVectorControlPhase phase, ReplicatedOperation operation, EventVectorFirstIssuance original,
        EventVectorAdmissionPolicy admission)
    {
        if (phase.Action is not (EventVectorControlAction.AbandonFirstIssuance
                or EventVectorControlAction.CleanupAbandonedIssuance)
            || phase.OriginalRequest.Action != EventFeedControlAction.Release
            || phase.OriginalRequest.CommandId == original.OriginalPublicCommandId
            || phase.OriginalFirstIssuancePublicCommandId != original.OriginalPublicCommandId
            || phase.OriginalRequest.OriginalFirstIssuancePublicCommandId != original.OriginalPublicCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        var native = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        admission.RequireEncodedBytes(native.Value.Length);
        var firstId = original.OriginalCapturedFirstOpenPhaseBytes.IsEmpty ? Guid.Empty
            : EventVectorControlOperationIdentity.Create(EventVectorFirstPhaseCarrier.Read(
                original.OriginalCapturedFirstOpenPhaseBytes, admission), principal.Id, admission).CommandId;
        var index = new EventVectorParentOutcomeIndex(Version, principal.Id, phase.OriginalRequest.CommandId,
            original.ControlPartition, original.MapId,
            EventVectorControlBodyProjection.RequestDigest(phase.OriginalRequest, admission),
            native.Value.ToArray(), operation.Id, NativeOperationFingerprint.Compute(operation))
        {
            OriginalAuthorizedSourceScopes = original.OriginalAuthorizedSourceScopes,
            OriginalFirstOpenPhaseBytes = original.OriginalCapturedFirstOpenPhaseBytes.ToArray(),
            OriginalFirstOpenNativeCommandId = firstId
        };
        _ = EventVectorEncodedRow.Create(EventVectorKeys.ParentOutcome(index.ControlPartition,
            principal.Id, index.OriginalPublicCommandId), index, admission);
        return index;
    }

    internal static void Write(IAtomicTransaction transaction, EventVectorParentOutcomeIndex index,
        EventVectorAdmissionPolicy admission)
        => EventVectorRowStorage.Write(transaction, EventVectorEncodedRow.Create(EventVectorKeys.ParentOutcome(
            index.ControlPartition, index.PrincipalId, index.OriginalPublicCommandId), index, admission));
}
