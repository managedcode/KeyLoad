using System.Buffers;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsValidation operations against the caller's database context.</summary>
public static class EventStreamsValidation
{
    internal const string RetainedDigestAlphabet = "0123456789abcdef";
    internal const int FirstRetainedDigestCharacter = 0;
    internal static readonly SearchValues<char> RetainedDigestCharacters = SearchValues.Create(RetainedDigestAlphabet);
    internal const int TopicPurgeGroupKeyComponents = 10;
    internal const int TopicPurgeGroupIdIndex = 9;
    internal const int TopicPurgePositiveIdentity = 1;
    internal const long TopicPurgeInitialCheckpoint = 0;
    internal static GroupState ReadTopicPurgePin(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, long tail)
    {
        try
        {
            var parts = KeyCodec.Decode(key);
            if (parts.Length != TopicPurgeGroupKeyComponents || parts[TopicPurgeGroupIdIndex] is not string id || !KeyCodec.Encode(parts).AsSpan().SequenceEqual(key))
            {
                throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
            }

            JsonData.Identifier(id);
            var group = OrleansSerialization.Deserialize<GroupState>(value);
            if (group.Generation < TopicPurgePositiveIdentity || group.OwnershipEpoch < TopicPurgePositiveIdentity || group.Checkpoint < TopicPurgeInitialCheckpoint || group.IssuedPosition < group.Checkpoint || group.IssuedPosition > tail || group.Definition is null || group.Definition.Policy is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
            }

            ValidateTopicPurgeDefinition(group.Definition);
            return group;
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.Validation or ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }
    }

    internal static void ValidateTopicPurgeDefinition(SubscriptionDefinition definition)
    {
        JsonData.Identifier(definition.DataPrincipalId);
        var policy = definition.Policy;
        if (policy.MaxWindow is < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroups.SubscriptionGroupsMaximumSubscriptionWindow || policy.MaxAttempts is < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroups.SubscriptionGroupsMaximumSubscriptionAttempts || policy.MaxLeaseSeconds is < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount or > SubscriptionGroups.SubscriptionGroupsMaximumSubscriptionLeaseSeconds || policy.RetryBaseMilliseconds < SubscriptionGroups.SubscriptionGroupsMinimumPositiveCount || policy.RetryMaxMilliseconds < policy.RetryBaseMilliseconds || definition.EventTypes.IsDefault || definition.EventTypes.Length > SubscriptionGroups.SubscriptionGroupsMaximumSubscriptionEventTypes || definition.EventTypes.Distinct(StringComparer.Ordinal).Count() != definition.EventTypes.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }

        foreach (var type in definition.EventTypes)
        {
            JsonData.Identifier(type);
        }
    }

    internal static RetainedTopicEventIdentity ReadPurgedTopicIdentity(ReadOnlySpan<byte> value, EventSourceRef source, long firstAvailablePosition)
    {
        try
        {
            var identity = OrleansSerialization.Deserialize<RetainedTopicEventIdentity>(value);
            if (identity.Generation != source.Generation || identity.Position < TopicPurgePositiveIdentity || identity.Position >= firstAvailablePosition || identity.ContentDigest is null || identity.ContentDigest.Length != TopicPublishReads.RetainedTopicDigestCharacters || identity.ContentDigest.AsSpan().IndexOfAnyExcept(RetainedDigestCharacters) >= FirstRetainedDigestCharacter)
            {
                throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
            }

            return identity;
        }
        catch (KeyLoadException failure) when (failure.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }
    }
}
