using KeyLoad.Core;
using KeyLoad.Core.Features.AnalyticsStore;
using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.AnalyticsStore.AnalyticsSharedProtocol;

namespace KeyLoad.UnitTests.Features.AnalyticsStore;

internal static class AnalyticsSharedAssertions
{
    internal static async Task BindingsAsync(TestDatabase fixture, Guid commandId)
    {
        var operation = fixture.ReadRetainedEntry(commandId).Operation!;
        var captured = ResourceEffectAdmissionValidation.Read(operation, new DatabaseLimits());
        var expected = fixture.Store.Read(view => new[] { P.Store, P.Documents }
            .Order(StringComparer.Ordinal).Select(name => ResourceLifetimeCatalog.Read(view,
                fixture.Partition.TenantId, fixture.Partition.DatabaseId, name,
                fixture.Database.Resource(view, fixture.Partition, name))!).ToArray());
        await Assert.That(captured.Error).IsNull();
        await SameAsync(expected, captured.Bindings.ToArray());
    }

    internal static async Task PageAsync(TestDatabase fixture, AnalyticsEventData expected, CancellationToken token)
    {
        var position = fixture.Store.Position;
        var image = KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store);
        var page = fixture.Database.ReadAnalyticsEvents(P.Root,
            new(fixture.Partition, P.Store, P.Epoch.AddSeconds(-1), P.Epoch.AddSeconds(1), 10, null), token);
        await SameAsync(new AnalyticsEventsPage([new(expected.Id, expected.Timestamp, expected.Dimensions,
            expected.Metrics, null, expected.Links, 1)], false, null), page);
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

    internal static OutboxEntry DocumentEntry(long sequence, string id, CommitToken token, PartitionRef partition, DateTimeOffset evaluatedAt, int ordinal = 0)
        => new(sequence, ordinal, token, evaluatedAt, new PutDocument(P.Documents, id, P.Json, 0),
            new(P.PutKind, P.Documents, id, 1), After: new(new(partition, P.Documents, id), 1, P.Json, new(), evaluatedAt));

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
}
