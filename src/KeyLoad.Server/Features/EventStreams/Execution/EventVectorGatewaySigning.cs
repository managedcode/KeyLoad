using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorGatewaySigning
{
    private const string InvalidParent = "The event feed parent request is inconsistent.";

    internal static string Create(GrainRequestCodec codec, Guid actualRequestId,
        string actualPrincipalId, OperationKind kind, Guid publicCommandId, ReadOnlyMemory<byte> payload)
    {
        if (kind != OperationKind.EventFeedControl)
        {
            return codec.CreateCommand(actualRequestId, actualPrincipalId, kind, publicCommandId, payload);
        }
        var original = GrainNativePayload.ReadCommand<EventFeedControlRequest>(payload);
        if (original.Action is not (EventFeedControlAction.Open or EventFeedControlAction.Offer
            or EventFeedControlAction.Acknowledge or EventFeedControlAction.Refresh
            or EventFeedControlAction.Release or EventFeedControlAction.Tail) || original.CommandId != publicCommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidParent);
        }
        return codec.CreateEventFeedControlParent(actualRequestId, actualPrincipalId, original);
    }

    internal static bool IsReceivingOnly(OperationKind? kind, ReadOnlyMemory<byte> payload)
        => kind == OperationKind.EventFeedControl
            && GrainNativePayload.ReadCommand<EventFeedControlRequest>(payload).Action == EventFeedControlAction.Tail;

    internal static void RefuseIncompleteOpen(OperationKind? kind, ReadOnlyMemory<byte> originalPayload, GrainOperationReply actualReply)
    {
        if (kind != OperationKind.EventFeedControl || actualReply.Error is not null)
        {
            return;
        }
        var original = GrainNativePayload.ReadCommand<EventFeedControlRequest>(originalPayload);
        if (GrainNativePayload.Read<GrainValue>(actualReply.Payload).Value is not EventFeedControlResult result
            || result.Version != EventFeedOpenProtocol.Version || result.Receipt is null
            || (original.Action != EventFeedControlAction.Tail && result.PublicCommandId != original.CommandId)
            || result.PublicCommandId == Guid.Empty || result.MapId != original.MapId
            || (original.Action == EventFeedControlAction.Release
                ? result.Released == result.Retained : result.Released || !result.Retained))
        {
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, InvalidParent);
        }
    }
}
