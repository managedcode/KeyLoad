using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>One unique real partition with independently configured resources and fixed observable payloads.</summary>
internal sealed record IsolatedKeyLoadPublicRegressionScenario(PartitionRef Partition)
{
    private const string Collection = "publicdocs";
    private const string StreamSet = "publicevents";
    private const string Queue = "publicqueue";
    private const string BlobStore = "publicblobs";

    internal string PrivateValue { get; } = "isolated-private-value";
    internal string InitialJson { get; } = "{\"value\":\"isolated-private-value\",\"oldField\":true}";
    internal string UpdatedJson { get; } = "{\"value\":\"isolated-updated-value\",\"newField\":42}";
    internal string SentinelJson { get; } = "{\"value\":\"isolated-unaffected-value\"}";
    internal string EventId { get; } = "public-event";
    internal string MessageId { get; } = "public-message";
    internal EntityRef Document => new(Partition, Collection, "changed");
    internal EntityRef Sentinel => new(Partition, Collection, "unaffected");
    internal EntityRef Missing => new(Partition, Collection, "missing");
    internal StreamRef Stream => new(Partition, StreamSet, "public-stream");
    internal QueueLaneRef Lane => new(Partition, Queue);
    internal InspectMessageRequest Inspect => new(Lane, MessageId);
    internal BlobRef Blob => new(Partition, BlobStore, "public-object");

    internal CommandRequest Command(params Mutation[] mutations) => new(Guid.NewGuid(), Partition, [.. mutations]);

    internal static async Task<IsolatedKeyLoadPublicRegressionScenario> CreateAsync(KeyLoadClient sdk, CancellationToken token)
    {
        var partition = new PartitionRef("isolated-public-" + Guid.NewGuid().ToString("N"),
            "public-regression", "public-domain", Guid.NewGuid().ToString("N"));
        foreach (var (name, kind) in new[]
        {
            (Collection, ResourceKind.Collection), (StreamSet, ResourceKind.StreamSet),
            (Queue, ResourceKind.WorkQueue), (BlobStore, ResourceKind.BlobStore)
        })
        {
            var expected = new ResourceDefinition(name, kind, partition.TransactionDomainId);
            var configured = await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, expected), token));
            await Assert.That(configured.Name).IsEqualTo(name);
            await Assert.That(configured.Kind).IsEqualTo(kind);
            await Assert.That(configured.TransactionDomainId).IsEqualTo(partition.TransactionDomainId);
        }
        return new(partition);
    }
}
