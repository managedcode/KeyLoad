using KeyLoad.UnitTests.Features.Authorization;
using P = KeyLoad.UnitTests.Features.Messaging.BudgetedQueueReceiveOutcomeProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class BudgetedQueueReceiveOutcomeAssertions
{
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();

    internal static async Task ReceiveAsync(BudgetedQueueReceiveOutcomeFixture fixture,
        ReplicatedOperation operation, OperationResult result, string messageId)
    {
        var actual = result.Get<ReceiveResult>();
        var delivery = await Assert.That(actual.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEmpty();
        var entry = fixture.Database.ReadRetainedEntry(operation);
        var appended = entry.Operation ?? throw new InvalidOperationException("The native receive entry is absent.");
        await Assert.That(appended.Kind).IsEqualTo(operation.Kind);
        await Assert.That(appended.PrincipalId).IsEqualTo(operation.PrincipalId);
        await Assert.That(appended.PayloadJson).IsEqualTo(operation.PayloadJson);
        await Assert.That(delivery.HeadersJson).IsEqualTo(P.CanonicalHeaders);
        var expected = new ReceiveResult(operation.Id,
            [new(messageId, P.Payload, P.CanonicalHeaders, delivery.Token, P.Initial,
                appended.EvaluatedAt.AddSeconds(fixture.Resource.QueuePolicy.MaxLeaseSeconds),
                (int)P.Initial, P.Initial)],
            new(fixture.Database.Store.Identity.Incarnation, fixture.Database.Partition.AtomicPartitionId,
                entry.Index, P.Initial));
        await EqualAsync(expected, actual);
    }

    internal static async Task UnchangedAsync(BudgetedQueueReceiveOutcomeFixture fixture, Func<Task> body)
    {
        var database = fixture.Database;
        var identity = NativeSerialization.Serialize(database.Store.Identity);
        var rows = TeamRowPolicyAssertions.Image(database.Store);
        var position = database.Store.Position;
        var applied = database.Database.LastApplied;
        var journal = database.ReadJournalCut();
        var outbox = database.Database.GetOutboxStatus(P.Root, database.Partition);
        var outcome = OutcomeBytes(fixture);
        await Assert.That(NativeSerialization.Serialize(fixture.OriginalIntent)
            .SequenceEqual(fixture.OriginalIntentBytes)).IsTrue();
        await body();
        await Assert.That(NativeSerialization.Serialize(fixture.OriginalIntent)
            .SequenceEqual(fixture.OriginalIntentBytes)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(database.Store.Identity).SequenceEqual(identity)).IsTrue();
        await Assert.That(TeamRowPolicyAssertions.Image(database.Store).SequenceEqual(rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(database.Database.LastApplied).IsEqualTo(applied);
        await Assert.That(database.ReadJournalCut()).IsEqualTo(journal);
        await EqualAsync(outbox, database.Database.GetOutboxStatus(P.Root, database.Partition));
        await Assert.That(OutcomeBytes(fixture).SequenceEqual(outcome)).IsTrue();
    }

    private static byte[] OutcomeBytes(BudgetedQueueReceiveOutcomeFixture fixture)
        => fixture.Database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.PartitionKey(
            fixture.Database.Partition, P.Worker, fixture.OriginalIntent.Id)))
            ?? throw new InvalidOperationException("The genuine original receive outcome is absent.");
}
