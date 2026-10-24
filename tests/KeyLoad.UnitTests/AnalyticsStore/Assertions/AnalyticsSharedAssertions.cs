using KeyLoad.Core.AnalyticsStore;
using KeyLoad.Core.ResourceExecution;
using P = KeyLoad.UnitTests.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.AnalyticsStore;

internal static class AnalyticsSharedAssertions
{
    internal static async Task BindingsAsync(TestDatabase fixture, Guid commandId)
    {
        var operation = fixture.ReadStoredOutcome(commandId).PreparedCommand!;
        var captured = ResourceEffectAdmissionValidation.Read(operation, new DatabaseLimits());
        var expected = new[]
        {
            AnalyticsExpectedResourceCreation.Lifetime(fixture, AnalyticsExpectedResourceCreation.SharedStore),
            AnalyticsExpectedResourceCreation.Lifetime(fixture, AnalyticsExpectedResourceCreation.SharedDocuments)
        }.OrderBy(lifetime => lifetime.ResourceName, StringComparer.Ordinal).ToArray();
        await Assert.That(captured.Error).IsNull();
        await AnalyticsStoreTestOperations.EqualAsync(expected, captured.Bindings.ToArray());
    }

    internal static async Task PageAsync(TestDatabase fixture, AnalyticsEventData expected, CancellationToken token)
    {
        var position = fixture.Store.Position;
        var image = KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store);
        var page = fixture.Database.ReadAnalyticsEvents(P.Root,
            new(fixture.Partition, P.Store, P.Epoch.AddSeconds(-1), P.Epoch.AddSeconds(1), 10, null), token);
        await AnalyticsStoreTestOperations.EqualAsync(new AnalyticsEventsPage([new(expected.Id, expected.Timestamp, expected.Dimensions,
            expected.Metrics, null, [.. expected.Links.Select(link => AnalyticsExpectedResourceCreation.Bind(fixture, link,
                AnalyticsExpectedResourceCreation.SharedDocuments))], 1)], false, null), page);
        var generation = fixture.Store.Read(view => OrleansSerialization.Deserialize<long>(
            view.ReadOwnedValue(AnalyticsStoreKeys.Generation(fixture.Partition, P.Store))!));
        await Assert.That(generation).IsEqualTo(1L);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(KeyLoad.UnitTests.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store)
            .SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task ReplayAsync(TestDatabase fixture, CommandRequest command, CommitReceipt expected)
    {
        var actual = fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await SameAsync(expected, actual);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences.IsEmpty).IsTrue();
        }
    }

    internal static OutboxEntry DocumentEntry(TestDatabase fixture, long sequence, string id, CommitToken token, PartitionRef partition, DateTimeOffset evaluatedAt, int ordinal = 0)
        => new(sequence, ordinal, token, evaluatedAt, new PutDocument(P.Documents, id, P.Json, 0),
            new(P.PutKind, P.Documents, id, 1), After: new(AnalyticsExpectedResourceCreation.Bind(fixture, new(partition, P.Documents, id),
                AnalyticsExpectedResourceCreation.SharedDocuments), 1, P.Json, new(), evaluatedAt));

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(Convert.ToHexString(OrleansSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(OrleansSerialization.Serialize(expected)));
}
