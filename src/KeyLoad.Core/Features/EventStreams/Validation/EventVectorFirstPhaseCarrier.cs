namespace KeyLoad.Core;

internal static class EventVectorFirstPhaseCarrier
{
    private const string Invalid = "The original event vector first phase carrier is inconsistent.";

    internal static ReadOnlyMemory<byte> SemanticDigest(ReadOnlyMemory<byte> originalBytes,
        EventVectorAdmissionPolicy admission)
    {
        if (originalBytes.IsEmpty)
        { return ReadOnlyMemory<byte>.Empty; }
        var first = Read(originalBytes, admission);
        return EventVectorControlBodyProjection.Digest(first, admission);
    }

    internal static EventVectorControlPhase Read(ReadOnlyMemory<byte> originalBytes,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(originalBytes.Length);
        if (originalBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var first = NativeSerialization.Deserialize<EventVectorControlPhase>(originalBytes.Span);
        if (first is null || first.Action != EventVectorControlAction.Open
            || first.OriginalRequest is null || first.OriginalRequest.Action != EventFeedControlAction.Open
            || !first.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || first.OriginalFirstIssuancePublicCommandId != Guid.Empty)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        EventVectorControlFields.Require(first, admission);
        return first;
    }

    internal static void Require(EventVectorControlPhase phase, EventVectorAdmissionPolicy admission)
    {
        var action = phase.Action;
        var carries = action is EventVectorControlAction.CaptureFirstPhase
            or EventVectorControlAction.ReserveFirstDispatch;
        var optional = action is EventVectorControlAction.AbandonFirstIssuance
            or EventVectorControlAction.CleanupAbandonedIssuance;
        var freshRelease = optional;
        if (carries && phase.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || !carries && !optional && !phase.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || freshRelease != (phase.OriginalFirstIssuancePublicCommandId != Guid.Empty))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        if (phase.OriginalCapturedFirstOpenPhaseBytes.IsEmpty)
        { return; }
        var first = Read(phase.OriginalCapturedFirstOpenPhaseBytes, admission);
        var request = phase.OriginalRequest;
        var originalId = freshRelease ? phase.OriginalFirstIssuancePublicCommandId : request.CommandId;
        if (first.OriginalRequest.CommandId != originalId || first.OriginalRequest.MapId != request.MapId
            || first.OriginalRequest.ControlPartition != request.ControlPartition
            || first.OriginalRequest.Scope != request.Scope
            || !freshRelease && (first.OriginalParentExpiresAt != phase.OriginalParentExpiresAt
                || first.OriginalParentRequestId != phase.OriginalParentRequestId))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
    }
}
