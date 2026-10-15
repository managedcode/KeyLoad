using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeBodyOwnershipAssertions
{
    internal static async Task CallerBuffersCannotChangeNativeProducerBodiesAsync(EventQueueStateEnvelopeFixture fixture,
        EventQueueStateEnvelopeSample sample)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        foreach (var pair in sample.Bodies)
        {
            var key = Convert.FromHexString(pair.Key);
            var owned = Required(fixture, key);
            await Assert.That(owned.AsSpan().SequenceEqual(pair.Value)).IsTrue();
            Array.Fill(owned, MutatedCallerByte);
            await Assert.That(Required(fixture, key).AsSpan().SequenceEqual(pair.Value)).IsTrue();
        }
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await EventQueueStateEnvelopeSourceAssertions.CurrentAsync(fixture, sample);
    }
}
