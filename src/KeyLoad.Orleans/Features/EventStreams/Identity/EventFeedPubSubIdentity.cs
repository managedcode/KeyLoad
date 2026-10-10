using System.Text;

namespace KeyLoad.Orleans;

internal static class EventFeedPubSubIdentity
{
    private static readonly byte[] OriginalKeyPrefix = Encoding.UTF8.GetBytes(
        EventFeedPubSubProtocol.ProviderName + EventFeedPubSubProtocol.KeySeparator);
    private static readonly GrainType OriginalGrainType = GrainType.Create(EventFeedPubSubProtocol.GrainTypeName);

    internal static void Require(string stateName, GrainId grainId)
    {
        if (stateName != EventFeedPubSubProtocol.StateName || grainId.Type != OriginalGrainType
            || !grainId.Key.AsSpan().StartsWith(OriginalKeyPrefix)
            || grainId.Key.AsSpan().Length == OriginalKeyPrefix.Length)
        { throw Errors.Fail(ErrorCode.Validation, EventFeedPubSubProtocol.Invalid); }
    }
}
