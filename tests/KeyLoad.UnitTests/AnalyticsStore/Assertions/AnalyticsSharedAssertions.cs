using KeyLoad.Core;
using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

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
        await AnalyticsStoreNativeTestOperations.EqualAsync(expected, captured.Bindings.ToArray());
    }

    internal static async Task PageAsync(TestDatabase fixture, AnalyticsEventData expected, CancellationToken token)
    {
        var position = fixture.Store.Position;
        var image = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store);
        var page = fixture.Database.ReadAnalyticsEvents(P.Root,
            new(fixture.Partition, P.Store, P.Epoch.AddSeconds(-1), P.Epoch.AddSeconds(1), 10, null), token);
        await AnalyticsStoreNativeTestOperations.EqualAsync(new AnalyticsEventsPage([new(expected.Id, expected.Timestamp, expected.Dimensions,
            expected.Metrics, null, [.. expected.Links.Select(link => AnalyticsExpectedResourceCreation.Bind(fixture, link,
                AnalyticsExpectedResourceCreation.SharedDocuments))], 1)], false, null), page);
        var generation = fixture.Store.Read(view => NativeSerialization.Deserialize<long>(
            view.ReadOwnedValue(AnalyticsStoreKeys.Generation(fixture.Partition, P.Store))!));
        await Assert.That(generation).IsEqualTo(1L);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store)
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
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
