using KeyLoad.Core;
using static KeyLoad.UnitTests.Features.EventStreams.TopicRetentionCorruptionFixture;
namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class TopicRetentionPinCorruptionTests
{
    [Test]
    [Arguments("checkpoint")]
    [Arguments("issued")]
    [Arguments("generation")]
    [Arguments("epoch")]
    [Arguments("policy")]
    [Arguments("alias")]
    [Arguments("key")]
    public async Task AcEventRetention005MalformedNativePinsCannotAdmitPurgeOrMutateAnyModel(string kind)
    {
        using var fixture = new TopicRetentionFixture();
        fixture.ReleasePins();
        var key = GroupKey(fixture);
        var original = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var state = NativeSerialization.Deserialize<GroupState>(original);
        var corrupt = kind switch
        {
            "checkpoint" => state with { Checkpoint = 4, IssuedPosition = 4 },
            "issued" => state with { IssuedPosition = 2 },
            "generation" => state with { Generation = 0 },
            "epoch" => state with { OwnershipEpoch = 0 },
            "policy" => state with { Definition = state.Definition with { Policy = state.Definition.Policy with { MaxWindow = 0 } } },
            _ => state
        };
        if (kind == "key")
        { key = KeySpace.Partition("subscription", fixture.Owner.Partition, "topic", "Topic", null, 1L, "first", "extra"); }
        Write(fixture, key, kind == "alias" ? NativeSerialization.Serialize(new TopicHead(3, 1, 1, 0)) : NativeSerialization.Serialize(corrupt));
        var command = fixture.Purge();
        await TopicRetentionCorruptionAssertions.RejectAsync(fixture, command);
        if (kind == "key")
        { Delete(fixture, key); }
        else
        { Write(fixture, key, original); }
        await TopicRetentionCorruptionAssertions.PurgeRepairedAsync(fixture, command);
        await HealthyAsync(fixture);
    }
}
