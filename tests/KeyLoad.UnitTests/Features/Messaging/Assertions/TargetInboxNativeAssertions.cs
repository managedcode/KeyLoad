using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;
namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxNativeAssertions
{
    private const int ExpectedEventCount = 1;
    internal static string[] Image(ZoneTreeStore store, params PartitionRef[] partitions)
        => store.Read(view => partitions.SelectMany(partition => PartitionRecordFamilies.All
            .Where(family => family != PartitionRecordFamilies.OutcomeV2 && family != PartitionRecordFamilies.OutcomeLocator
                && family != PartitionRecordFamilies.OutcomeLocatorV2)
            .SelectMany(family =>
            {
                var page = view.Scan(KeySpace.Partition(family, partition), TargetInboxUnitProtocol.MaximumFixtureRows);
                if (page.HasMore)
                { throw new InvalidOperationException("Target inbox fixture exceeded its closed row bound."); }
                return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span));
            })).ToArray());

    internal static async Task RefusedAsync(DatabaseEngine database, ZoneTreeStore store, CommitInboxRequest request,
        ErrorCode expected, CancellationToken token, string principal = TargetInboxUnitProtocol.Root)
    {
        var before = Image(store, request.Source.Partition, request.Target.Partition);
        await Assert.That(TargetInboxNativeSetup.Apply(database, request, token, principal).Error).IsEqualTo(expected);
        await Assert.That(Image(store, request.Source.Partition, request.Target.Partition)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    internal static async Task EffectsAsync(DatabaseEngine database, CommitInboxRequest request)
    {
        var document = database.GetDocument(TargetInboxUnitProtocol.Root,
            new(request.Target.Partition, TargetInboxUnitProtocol.Collection, TargetInboxUnitProtocol.Document));
        await Assert.That(document!.Revision).IsEqualTo(TargetInboxUnitProtocol.FirstRevision);
        await Assert.That(document.Json).IsEqualTo(TargetInboxUnitProtocol.Payload);
        var output = database.InspectMessage(TargetInboxUnitProtocol.Root,
            new(request.Target.Partition, TargetInboxUnitProtocol.Output), TargetInboxUnitProtocol.Message);
        await Assert.That(output!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(output.PayloadJson).IsEqualTo(TargetInboxUnitProtocol.Payload);
        var events = database.ReadStream(TargetInboxUnitProtocol.Root,
            new StreamRef(request.Target.Partition, TargetInboxUnitProtocol.Events, TargetInboxUnitProtocol.Stream));
        await Assert.That(events.Events.Length).IsEqualTo(ExpectedEventCount);
        await Assert.That(events.Events[0].Data.PayloadJson).IsEqualTo(TargetInboxUnitProtocol.Payload);
    }

    internal static async Task CapacityAsync(ZoneTreeStore store, CommitInboxRequest request, long count)
    {
        var actual = store.Read(view => (Capacity: global::KeyLoad.Storage.StorageRecords.GetRecord<TargetInboxCapacity>(view, TargetInboxStorage.CapacityKey(request.Target)),
            Row: view.ReadOwnedValue(TargetInboxStorage.Key(request))));
        await Assert.That(actual.Capacity!.Count).IsEqualTo(count);
        var retained = store.Read(view => view.Scan(KeySpace.Partition(TargetInboxProtocol.RecordSpace,
            request.Target.Partition, request.Target.Queue), TargetInboxUnitProtocol.MaximumFixtureRows));
        await Assert.That(retained.HasMore).IsFalse();
        await Assert.That(retained.Records.Length).IsEqualTo(checked((int)count));
        await Assert.That(actual.Capacity.Bytes).IsEqualTo(retained.Records.Sum(row => (long)row.Key.Length + row.Value.Length));
        await Assert.That(actual.Row).IsNotNull();
    }
}
