using System.Buffers;
using KeyLoad.Storage;
namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string RetainedDigestAlphabet = "0123456789abcdef";
    private const int FirstRetainedDigestCharacter = 0;
    private static readonly SearchValues<char> RetainedDigestCharacters = SearchValues.Create(RetainedDigestAlphabet);
    private const int TopicPurgeGroupKeyComponents = 10;
    private const int TopicPurgeGroupIdIndex = 9;
    private const int TopicPurgePositiveIdentity = 1;
    private const long TopicPurgeInitialCheckpoint = 0;
    private static GroupState ReadTopicPurgePin(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long tail)
    {
        try
        {
            var parts = KeyCodec.Decode(key);
            if (parts.Length != TopicPurgeGroupKeyComponents || parts[TopicPurgeGroupIdIndex] is not string id
                || !KeyCodec.Encode(parts).AsSpan().SequenceEqual(key))
            { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
            JsonData.Identifier(id);
            var group = NativeSerialization.Deserialize<GroupState>(value);
            if (group.Generation < TopicPurgePositiveIdentity || group.OwnershipEpoch < TopicPurgePositiveIdentity
                || group.Checkpoint < TopicPurgeInitialCheckpoint || group.IssuedPosition < group.Checkpoint || group.IssuedPosition > tail
                || group.Definition is null || group.Definition.Policy is null)
            { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
            ValidateTopicPurgeDefinition(group.Definition);
            return group;
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.Validation or ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
    }
    private static void ValidateTopicPurgeDefinition(SubscriptionDefinition definition)
    {
        JsonData.Identifier(definition.DataPrincipalId);
        var policy = definition.Policy;
        if (policy.MaxWindow is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionWindow
            || policy.MaxAttempts is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionAttempts
            || policy.MaxLeaseSeconds is < SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroupsMaximumSubscriptionLeaseSeconds
            || policy.RetryBaseMilliseconds < SubscriptionGroupsMinimumPositiveCount || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds
            || definition.EventTypes.IsDefault || definition.EventTypes.Length > SubscriptionGroupsMaximumSubscriptionEventTypes
            || definition.EventTypes.Distinct(StringComparer.Ordinal).Count() != definition.EventTypes.Length)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
        foreach (var type in definition.EventTypes)
        { JsonData.Identifier(type); }
    }
    private static RetainedTopicEventIdentity ReadPurgedTopicIdentity(ReadOnlySpan<byte> value,
        EventSourceRef source, long firstAvailablePosition)
    {
        try
        {
            var identity = NativeSerialization.Deserialize<RetainedTopicEventIdentity>(value);
            if (identity.Generation != source.Generation || identity.Position < TopicPurgePositiveIdentity
                || identity.Position >= firstAvailablePosition || identity.ContentDigest is null
                || identity.ContentDigest.Length != RetainedTopicDigestCharacters
                || identity.ContentDigest.AsSpan().IndexOfAnyExcept(RetainedDigestCharacters) >= FirstRetainedDigestCharacter)
            { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
            return identity;
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
    }
}
