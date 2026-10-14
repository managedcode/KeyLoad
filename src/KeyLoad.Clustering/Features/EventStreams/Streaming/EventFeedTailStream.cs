using KeyLoad.Core;
using Orleans.Streams;

namespace KeyLoad.Clustering;

internal static class EventFeedTailStream
{
    private const int HexCharactersPerByte = 2;

    internal static IAsyncStream<EventFeedWakeupHint> Resolve(global::Orleans.Runtime.IGrainContext context,
        EventFeedControlRequest request, ReadExecutionBudget work)
    {
        if (context.GrainInstance is not global::Orleans.IGrainBase originalOwner)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var key = EventVectorKeys.Map(request.ControlPartition, request.MapId);
        var bytes = checked((long)key.Length * sizeof(char) * HexCharactersPerByte);
        work.ChargeBytes(bytes);
        var streamKey = Convert.ToHexString(key);
        var provider = global::Orleans.GrainStreamingExtensions.GetStreamProvider(originalOwner,
            EventFeedPubSubProtocol.ProviderName);
        return provider.GetStream<EventFeedWakeupHint>(global::Orleans.Runtime.StreamId.Create(
            EventFeedPubSubProtocol.ProviderName, streamKey));
    }
}
