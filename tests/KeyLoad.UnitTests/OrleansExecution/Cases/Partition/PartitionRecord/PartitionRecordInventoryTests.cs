using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.RuntimeJournaling.Contracts;
using KeyLoad.Core.Features.StoragePartitions.Contracts;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class PartitionRecordInventoryTests
{
    private const string Suffix = "inventory-probe";
    private const string Value = "raw-value";
    internal const int ExpectedFamilyCount = 71;
    private static readonly PartitionRef Partition = new(PartitionRecordFixture.TenantId,
        PartitionRecordFixture.DatabaseId, PartitionRecordFixture.DomainId, PartitionRecordFixture.PartitionKey);
    internal static readonly string[] ExpectedFamilies =
    [
        "adjacency", "aggregate-snapshot", "analytics-event", "analytics-generation", "blob-head", "blob-part", "blob-partmeta",
        "blob-state", "bm25-corpus-current", "dead-letter", "document", "document-epoch", "edge", "event", "event-feed",
        "event-id", "event-sequence", "graph-cross-partition-capacity", "graph-cross-partition-intent",
        "graph-cross-reverse-adjacency", "graph-edge-owner-version",
        "inbox", "index", "lease", "message-body", "message-meta",
        "online-text-current", "outbox", "outbox-head", "outcome-locator", "outcome-locator", "outcome",
        "processing-inbox", "processing-inbox-capacity", "projection-consumer", "projection-receipt", "queue-counters",
        "queue-dead-letter-order", "queue-order", "queue-pending-dead-letter",
        "queue-transfer-intent", "queue-transfer-source-capacity", "queue-transfer-target-capacity",
        "queue-transfer-target-receipt", "ready", "recurring-saga-capacity", "recurring-schedule",
        "saga-state", "sample", "sample-chunk-block", "sample-chunk-correction", "sample-chunk-manifest",
        "sample-chunk-window", "sample-id", "sample-retention", "sample-rollup", "sample-sequence", "scheduled",
        "stream-head", "subscription", "subscription-completion", "subscription-inbox",
        "subscription-window", "topic-event", "topic-event-id", "topic-head", "unique", "vector",
        "vector-projection-effect", "vector-projection-lineage", "visibility-epoch"
    ];

    [Test]
    public async Task AcPmove001InventoryAndPageCoverEveryPartitionFamily()
    {
        using var fixture = new PartitionRecordFixture();
        await Assert.That(PartitionRecordFamilies.All).IsEquivalentTo(ExpectedFamilies,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(PartitionRecordFamilies.All.Length).IsEqualTo(ExpectedFamilyCount);
        await Assert.That(ExpectedFamilies.Length).IsEqualTo(ExpectedFamilyCount);
        await Assert.That(PartitionRecordFamilies.All.SequenceEqual(
            PartitionRecordFamilies.All.Order(StringComparer.Ordinal))).IsTrue();
        var entries = ExpectedFamilies.Select(family => PartitionRecordFixture.Row(
            family, Partition, Suffix, Value)).ToArray();
        PartitionRecordFixture.Seed(fixture.Store, entries);
        var inventory = PartitionRecordFamilies.All.Select(family =>
        {
            var page = PartitionRecordFixture.Read(fixture.Store, Partition, family, 1, 512, 512);
            var matches = page.Records.Length == 1
                && PartitionRecordFixture.Hex(page.Records[0].Key.Span)
                    == PartitionRecordFixture.Hex(PartitionRecordFixture.Key(family, Partition, Suffix))
                && PartitionRecordFixture.Hex(page.Records[0].Value.Span)
                    == PartitionRecordFixture.Hex(System.Text.Encoding.UTF8.GetBytes(Value));
            return matches ? 'T' : 'F';
        }).ToArray();
        await Assert.That(new string(inventory)).IsEqualTo(new string('T', ExpectedFamilies.Length));
    }
}
