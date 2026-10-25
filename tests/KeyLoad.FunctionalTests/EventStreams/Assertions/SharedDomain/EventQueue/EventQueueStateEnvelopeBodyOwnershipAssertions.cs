using KeyLoad.FunctionalTests.Messaging;
using TUnit.Assertions.Enums;
using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class EventQueueStateEnvelopeBodyOwnershipAssertions
{
    internal static async Task CallerBuffersCannotChangeProducerBodiesAsync(EventQueueStateEnvelopeFixture fixture,
        EventQueueStateEnvelopeState state)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        foreach (var pair in state.Bodies)
        {
            var key = Convert.FromHexString(pair.Key);
            var owned = Required(fixture, key);
            await Assert.That(owned.AsSpan().SequenceEqual(pair.Value)).IsTrue();
            Array.Fill(owned, MutatedCallerByte);
            await Assert.That(Required(fixture, key).AsSpan().SequenceEqual(pair.Value)).IsTrue();
        }
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await EventQueueStateEnvelopeSourceAssertions.CurrentAsync(fixture, state);
    }
}
