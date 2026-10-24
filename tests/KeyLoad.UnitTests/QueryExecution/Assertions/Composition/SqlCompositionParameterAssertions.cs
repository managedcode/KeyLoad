using KeyLoad.Storage;
using KeyLoad.UnitTests.Messaging;
using KeyLoad.UnitTests.StorageRecovery.Assertions;
using C = KeyLoad.UnitTests.DatabaseComposition.QueueGraphIdentityState;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlCompositionParameterAssertions
{
    internal static async Task RuntimeReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await EqualAsync(actual, expected);
        await Assert.That(actual.Mutations.Length).IsEqualTo(expected.Mutations.Length);
        for (var index = 0; index < expected.Mutations.Length; index++)
        {
            await MutationReceiptAsync(actual.Mutations[index], expected.Mutations[index]);
        }
    }

    internal static async Task ConserveAsync(SqlCompositionParameterFixture fixture, string[] image,
        long position, StoreIdentity identity)
    {
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(image,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, identity, position);
    }

    internal static async Task ModelsAsync(SqlCompositionParameterFixture fixture)
    {
        var seed = fixture.Seed;
        foreach (var expected in new[] { seed.First, seed.Second })
        {
            var actual = fixture.Store.Read(view => view.GetRecord<DocumentRecord>(
                KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(expected.Reference)));
            await EqualAsync(actual, expected);
            await EqualAsync(fixture.Database.GetDocument(C.Root, expected.Reference),
                new DocumentResult(expected.Reference, 1L, expected.Json, false, []));
        }
        await EqualAsync(fixture.Database.InspectMessage(C.Root, new(fixture.Source.Partition, C.Queue), C.Message), seed.Source);
        var forward = fixture.Originals.Count > 0;
        var reverse = fixture.Originals.Count > 1;
        var from = fixture.Source.ExpectedReference(seed.From);
        var to = fixture.Source.ExpectedReference(seed.To);
        var edge = new EdgeRecord(C.Prefix + C.Message, from, to, C.Label, C.AttributesJson, 1L);
        await EqualAsync(fixture.Store.Read(view => view.GetRecord<EdgeRecord>(
            KeySpace.Partition("edge", fixture.Source.Partition, C.Graph, edge.Id))), forward ? edge : null);
        await EqualAsync(fixture.Database.Traverse(C.Root, fixture.Source.Partition, C.Graph, seed.From),
            new KeyLoad.GraphTraversal(forward ? [from, to] : [from], forward ? [edge] : []));
        var payload = KeyLoad.UnitTests.DatabaseComposition.QueueGraphIdentityAssertions.Payload(
            new(from, to, C.Label, C.AttributesJson));
        var derived = new MessageInspection(new(SqlCompositionParameterData.ReversePrefix + edge.Id,
            MessageState.Ready, 0, 1L, 1L, null, null), payload, "{}");
        await EqualAsync(fixture.Database.InspectMessage(C.Root,
            new(fixture.Source.Partition, SqlCompositionParameterData.DerivedQueue), derived.Metadata.Id),
            reverse ? derived : null);
        var marker = new EntityRef(fixture.Source.Partition, C.Collection, C.Marker);
        await Assert.That(fixture.Store.Read(view => view.GetRecord<DocumentRecord>(
            KeyLoad.Core.DocumentStorage.DocumentStorageKeys.RecordKey(marker)))).IsNull();
    }

    internal static async Task OutboxAsync(SqlCompositionParameterFixture fixture)
    {
        var seed = fixture.Seed;
        var command = OrleansSerialization.Deserialize<CommandRequest>(seed.Seed.Value!.Input.Span);
        var expected = new List<OutboxEntry>
        {
            new(1L, 0, seed.SeedReceipt.Token, new DateTimeOffset(seed.Seed.Timestamp), command.Mutations[0], seed.SeedReceipt.Mutations[0], null, seed.First),
            new(2L, 1, seed.SeedReceipt.Token, new DateTimeOffset(seed.Seed.Timestamp), command.Mutations[1], seed.SeedReceipt.Mutations[1], null, seed.Second),
            new(3L, 2, seed.SeedReceipt.Token, new DateTimeOffset(seed.Seed.Timestamp), command.Mutations[2], seed.SeedReceipt.Mutations[2])
        };
        foreach (var original in fixture.Originals)
        {
            expected.Add(new(expected.Count + 1L, 0, original.Expected.Token, new DateTimeOffset(original.Operation.Timestamp),
                original.Effect, original.Expected.Mutations[0]));
        }
        var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition("outbox", fixture.Source.Partition),
            fixture.Database.Limits.MaxScanRecords));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(expected.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            var actual = OrleansSerialization.Deserialize<OutboxEntry>(page.Records[index].Value.Span);
            await EqualAsync(actual, expected[index]);
            await Assert.That(actual.Mutation.GetType()).IsEqualTo(expected[index].Mutation.GetType());
            await Assert.That(actual.Commit).IsEqualTo(expected[index].Commit);
            await MutationReceiptAsync(actual.Receipt, expected[index].Receipt);
        }
        var head = new OutboxHead(expected.Count, 1L, expected.Count, page.Records.Sum(value => (long)value.Value.Length));
        await EqualAsync(fixture.Database.GetOutboxStatus(C.Root, fixture.Source.Partition), new OutboxStatus(head, []));
    }

    private static async Task MutationReceiptAsync(MutationReceipt actual, MutationReceipt expected)
    {
        await EqualAsync(actual, expected);
        await Assert.That(actual.CompositionReferences.SequenceEqual(expected.CompositionReferences)).IsTrue();
    }

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(actual)))
            .IsEqualTo(System.Text.Encoding.UTF8.GetString(JsonDefaults.Serialize(expected)));
}
