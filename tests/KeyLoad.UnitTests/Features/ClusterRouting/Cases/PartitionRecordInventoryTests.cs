using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class PartitionRecordInventoryTests
{
    private const string Suffix = "inventory-probe";
    private const string Value = "raw-value";
    private const int ExpectedFamilyCount = 55;
    private static readonly PartitionRef Partition = new(PartitionRecordNativeFixture.TenantId,
        PartitionRecordNativeFixture.DatabaseId, PartitionRecordNativeFixture.DomainId, PartitionRecordNativeFixture.PartitionKey);
    private static readonly string[] ExpectedFamilies =
    [
        "adjacency", "aggregate-snapshot-v1", "blob-head-v1", "blob-part-v1", "blob-partmeta-v1",
        "blob-state-v1", "dead-letter", "document", "document-epoch", "edge", "event", "event-feed",
        "event-id", "event-sequence", "graph-cross-partition-capacity", "graph-cross-partition-intent",
        "graph-cross-reverse-adjacency", "graph-edge-owner-version", "inbox", "index", "lease", "message-body", "message-meta",
        "outbox", "outbox-head", "outcome-locator-v1", "projection-consumer", "projection-receipt", "queue-counters",
        "queue-transfer-intent", "queue-transfer-source-capacity", "queue-transfer-target-capacity",
        "queue-transfer-target-receipt", "ready", "recurring-saga-capacity", "recurring-schedule",
        "saga-state", "sample", "sample-id", "sample-retention-v1", "sample-sequence", "scheduled",
        "stream-head", "subscription", "subscription-completion", "subscription-inbox",
        "subscription-window", "topic-event", "topic-event-id", "topic-head", "unique", "vector",
        "vector-projection-effect", "vector-projection-lineage", "visibility-epoch"
    ];

    [Test]
    public async Task AcPmove001InventoryAndNativePageCoverEveryPartitionFamily()
    {
        using var fixture = new PartitionRecordNativeFixture();
        await Assert.That(PartitionRecordFamilies.All).IsEquivalentTo(ExpectedFamilies,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(PartitionRecordFamilies.All.Length).IsEqualTo(ExpectedFamilyCount);
        await Assert.That(ExpectedFamilies.Length).IsEqualTo(ExpectedFamilyCount);
        await Assert.That(PartitionRecordFamilies.All.SequenceEqual(
            PartitionRecordFamilies.All.Order(StringComparer.Ordinal))).IsTrue();
        var entries = ExpectedFamilies.Select(family => PartitionRecordNativeFixture.Row(
            family, Partition, Suffix, Value)).ToArray();
        PartitionRecordNativeFixture.Seed(fixture.Store, entries);
        var inventory = PartitionRecordFamilies.All.Select(family =>
        {
            var page = PartitionRecordNativeFixture.Read(fixture.Store, Partition, family, 1, 512, 512);
            var matches = page.Records.Length == 1
                && PartitionRecordNativeFixture.Hex(page.Records[0].Key.Span)
                    == PartitionRecordNativeFixture.Hex(PartitionRecordNativeFixture.Key(family, Partition, Suffix))
                && PartitionRecordNativeFixture.Hex(page.Records[0].Value.Span)
                    == PartitionRecordNativeFixture.Hex(System.Text.Encoding.UTF8.GetBytes(Value));
            return matches ? 'T' : 'F';
        }).ToArray();
        await Assert.That(new string(inventory)).IsEqualTo(new string('T', ExpectedFamilies.Length));
    }
}
