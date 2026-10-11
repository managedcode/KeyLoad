using KeyLoad.Core;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorAuthenticatedSourceObservation
{
    internal static EventVectorSourcePhase Read(EventVectorSourcePhase admitted,
        EventVectorRemoteCall originalCall, EventVectorMacCheckedReply authenticatedReply,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(originalCall);
        ArgumentNullException.ThrowIfNull(authenticatedReply);
        ArgumentNullException.ThrowIfNull(admission);
        var reply = authenticatedReply.Reply;
        if (originalCall.Action is not (EventVectorRemoteAction.SourceEffect or EventVectorRemoteAction.OriginalOutcome)
            || reply.Action != originalCall.Action || reply.RequestId != originalCall.RequestId
            || reply.Nonce != originalCall.Nonce || reply.Error is not null || reply.OriginalNativeReply.IsEmpty
            || authenticatedReply.OriginalReplyBytes.IsEmpty
            || string.IsNullOrWhiteSpace(authenticatedReply.OriginalSignature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, EventVectorRemoteProtocol.Invalid); }
        admission.RequireEncodedBytes(originalCall.OriginalNativeRequest.Length);
        var sent = NativeSerialization.Deserialize<EventVectorSourcePhase>(originalCall.OriginalNativeRequest.Span);
        if (sent is null || sent.PhaseCommandId != admitted.PhaseCommandId
            || !sent.OriginalNativeBody.Span.SequenceEqual(admitted.OriginalNativeBody.Span)
            || !sent.OriginalBodyDigest.Span.SequenceEqual(admitted.OriginalBodyDigest.Span))
        { throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid); }
        _ = EventVectorSourceMutationValidation.ReadOriginal(sent, admission);
        _ = EventVectorSourceMutationValidation.ReadOriginal(admitted, admission);
        admission.RequireEncodedBytes(reply.OriginalNativeReply.Length);
        var observation = NativeSerialization.Deserialize<EventVectorSourceObservation>(reply.OriginalNativeReply.Span)
            ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        return EventVectorObservedPhase.Create(admitted, observation,
            authenticatedReply.OriginalReplyBytes, admission);
    }
}
