using KeyLoad.FunctionalTests.Authorization;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using P = KeyLoad.FunctionalTests.Messaging.BudgetedQueueReceiveOutcomeProtocol;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class BudgetedQueueReceiveOutcomeAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();

    internal static async Task ReceiveAsync(BudgetedQueueReceiveOutcomeFixture fixture,
        Command<DatabaseCommand> operation, Result<object> result, string messageId)
    {
        var actual = result.Get<ReceiveResult>();
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEmpty();
        var entry = fixture.Database.ReadStoredOutcome(operation);
        var appended = entry.PreparedCommand ?? throw new InvalidOperationException("The execution receive entry is absent.");
        await Assert.That(appended.Value!.Kind).IsEqualTo(operation.Value!.Kind);
        await Assert.That(appended.UserId!).IsEqualTo(operation.UserId!);
        await Assert.That(appended.Value!.Input.Span.SequenceEqual(operation.Value!.Input.Span)).IsTrue();
        await Assert.That(delivery.HeadersJson).IsEqualTo(P.Headers);
        var expected = new ReceiveResult(operation.CommandId,
            [new(messageId, P.Payload, P.Headers, delivery.Token, P.Initial,
                new DateTimeOffset(appended.Timestamp).AddSeconds(fixture.Resource.QueuePolicy.MaxLeaseSeconds),
                (int)P.Initial, P.Initial)],
            new(fixture.Database.Store.Identity.Incarnation, fixture.Database.Partition.AtomicPartitionId,
                entry.CanonicalSequence));
        await EqualAsync(expected, actual);
    }

    internal static async Task UnchangedAsync(BudgetedQueueReceiveOutcomeFixture fixture, Func<Task> body)
    {
        var database = fixture.Database;
        var identity = OrleansSerialization.Serialize(database.Store.Identity);
        var rows = TeamRowPolicyAssertions.Image(database.Store);
        var position = database.Store.Position;
        var applied = database.Database.LastApplied;
        var journal = database.ReadCanonicalCut();
        var outbox = database.Database.GetOutboxStatus(P.Root, database.Partition);
        var outcome = OutcomeBytes(fixture);
        await Assert.That(OrleansSerialization.Serialize(fixture.OriginalIntent)
            .SequenceEqual(fixture.OriginalIntentBytes)).IsTrue();
        await body();
        await Assert.That(OrleansSerialization.Serialize(fixture.OriginalIntent)
            .SequenceEqual(fixture.OriginalIntentBytes)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(database.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(database.Store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(database.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(database.ReadCanonicalCut()).IsEqualTo(journal);
        await EqualAsync(outbox, database.Database.GetOutboxStatus(P.Root, database.Partition));
        await Assert.That(OutcomeBytes(fixture).SequenceEqual(outcome)).IsTrue();
    }

    private static byte[] OutcomeBytes(BudgetedQueueReceiveOutcomeFixture fixture)
        => fixture.Database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            fixture.Database.Partition, P.Worker, fixture.OriginalIntent.CommandId)))
            ?? throw new InvalidOperationException("The genuine original receive outcome is absent.");
}
