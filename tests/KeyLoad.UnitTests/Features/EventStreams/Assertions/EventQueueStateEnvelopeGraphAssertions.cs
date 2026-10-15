using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeGraphAssertions
{
    internal static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample,
        CancellationToken token)
    {
        var from = new EntityRef(fixture.Partition, Documents, Item);
        var to = new EntityRef(fixture.Partition, Documents, Target);
        var edge = new EdgeRecord(Item, from, to, Label, EmptyObject, InitialRevision);
        var key = KeySpace.Partition(EdgeFamily, fixture.Partition, Graph, Item);
        await Assert.That(Required(fixture, key).AsSpan().SequenceEqual(sample.GraphBytes)).IsTrue();
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(
            fixture.Store.Read(view => view.GetRecord<EdgeRecord>(key)), edge);
        var traversal = fixture.Database.Traverse(Root, fixture.Partition, Graph, from, cancellationToken: token);
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(traversal, new global::KeyLoad.GraphTraversal([from, to], [edge]));
        foreach (var family in new[] { EdgeFamily, AdjacencyFamily })
        {
            var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(family, fixture.Partition, Graph), ScanBound));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.Length).IsEqualTo(family == EdgeFamily ? SingleResult : TwoVertices);
        }
    }
}
