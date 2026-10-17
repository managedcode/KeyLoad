using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapModels
{
    internal static async Task RequireAsync(MetadataBootstrapState state)
    {
        var database = state.Fixture.Database;
        var reference = new EntityRef(state.Partition, MetadataBootstrapState.Collection, MetadataBootstrapState.Entity);
        await MetadataBootstrapValues.EqualAsync(new DocumentResult(reference, 1L, MetadataBootstrapState.Json, false, []),
            database.GetDocument(MetadataBootstrapState.Actor, reference));
        var stream = new StreamRef(state.Partition, MetadataBootstrapState.Streams, MetadataBootstrapState.Entity);
        var page = database.ReadStream(MetadataBootstrapState.Actor, stream);
        await Assert.That(page.Stream).IsEqualTo(stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(1L, 1L, 1L));
        await Assert.That(page.CutPosition).IsEqualTo(state.Fixture.Store.Position);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(state.Fixture.Store.Position);
        var data = new EventData(MetadataBootstrapState.Entity, "Created", MetadataBootstrapState.Json);
        await MetadataBootstrapValues.EqualAsync(new[] { new EventRecord(stream, 1L, 1L, data, state.Command.EvaluatedAt) }, page.Events.ToArray());
        await MetadataBootstrapValues.EqualAsync(new MessageInspection(new(MetadataBootstrapState.Entity, MessageState.Ready,
            0, 1L, 1L, null, null), MetadataBootstrapState.Json, "{}"),
            database.InspectMessage(MetadataBootstrapState.Actor, new(state.Partition, MetadataBootstrapState.Queue), MetadataBootstrapState.Entity));
        await OutboxAsync(state, reference);
    }
    private static async Task OutboxAsync(MetadataBootstrapState state, EntityRef reference)
    {
        var bytes = 0L;
        for (var ordinal = 0; ordinal < state.Request.Mutations.Length; ordinal++)
        {
            var sequence = ordinal + 1L;
            var raw = state.Fixture.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox", state.Partition, sequence)))
                ?? throw new InvalidOperationException("An acknowledged metadata-continuation outbox row is absent.");
            bytes += raw.LongLength;
            var actual = NativeSerialization.Deserialize<OutboxEntry>(raw);
            await Assert.That(actual.Sequence).IsEqualTo(sequence);
            await Assert.That(actual.Ordinal).IsEqualTo(ordinal);
            await Assert.That(actual.Commit).IsEqualTo(state.Receipt.Token);
            await Assert.That(actual.CommittedAt).IsEqualTo(state.Command.EvaluatedAt);
            await MetadataBootstrapValues.EqualAsync<Mutation>(state.Request.Mutations[ordinal], actual.Mutation);
            await MetadataBootstrapValues.EqualAsync(state.Receipt.Mutations[ordinal], actual.Receipt);
            await Assert.That(actual.Receipt.CompositionReferences).IsEmpty();
            await Assert.That(actual.Before).IsNull();
            await MetadataBootstrapValues.EqualAsync(ordinal == 0 ? new DocumentRecord(reference, 1L,
                MetadataBootstrapState.Json, new(), state.Command.EvaluatedAt) : null, actual.After);
        }
        var status = state.Fixture.Database.GetOutboxStatus(MetadataBootstrapState.Actor, state.Partition);
        await MetadataBootstrapValues.EqualAsync(new OutboxHead(3L, 1L, 3L, bytes), status.Head);
        await Assert.That(status.Consumers).IsEmpty();
    }
}
