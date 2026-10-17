using KeyLoad.Core;
using static KeyLoad.UnitTests.Features.EventStreams.TopicRetentionCorruptionFixture;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionIdentityCorruptionTests
{
    [Test]
    [Arguments("nonhex")]
    [Arguments("uppercase")]
    [Arguments("alias")]
    [Arguments("position")]
    public async Task AcEventRetention005MalformedRetainedIdentityRejectsExactReceiptAndHealthyFreshEventContinues(string kind)
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        fixture.Submit(fixture.Purge()).Get<CommitReceipt>();
        var key = DigestKey(fixture, "event1");
        var original = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var state = NativeSerialization.Deserialize<RetainedTopicEventIdentity>(original);
        var bad = kind switch
        {
            "nonhex" => state with { ContentDigest = new string('g', 64) },
            "uppercase" => state with { ContentDigest = state.ContentDigest.ToUpperInvariant() },
            "position" => state with { Position = 3 },
            _ => state
        };
        Write(fixture, key, kind == "alias" ? NativeSerialization.Serialize(1L) : NativeSerialization.Serialize(bad));
        var request = new CommandRequest(Guid.NewGuid(), fixture.Owner.Partition, [new PublishTopic("topic", [new("event1", "Created", "{\"n\":1}")])]);
        await TopicRetentionCorruptionAssertions.RejectAsync(fixture, request);
        Write(fixture, key, original);
        await TopicRetentionAssertions.Reject(fixture, request, ErrorCode.DuplicateEventId);
        await TopicRetentionNativeState.VerifyAsync(fixture);
        await HealthyAsync(fixture);
    }
    [Test]
    public async Task AcEventRetention005PurgeNeverOverwritesAnExistingIdentityForAnActiveBody()
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        var key = DigestKey(fixture, "event1");
        Write(fixture, key, NativeSerialization.Serialize(new RetainedTopicEventIdentity(new string('0', 64), 1, 1)));
        var command = fixture.Purge();
        await TopicRetentionCorruptionAssertions.RejectAsync(fixture, command);
        Delete(fixture, key);
        await TopicRetentionCorruptionAssertions.PurgeRepairedAsync(fixture, command);
        await HealthyAsync(fixture);
    }
}
