using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadEventOperations
{
    private const int RequiredEventReadCount = 2;

    internal const string EventsName = "events";
    private const string EventType = "comparison-event";
    private const string CardinalityFailure = "KeyLoadStreamCardinality";
    internal const string WrongWriteProfile = "WrongWriteProfile";

    internal static async Task SeedAsync(KeyLoadClient client, PartitionRef partition, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        foreach (var document in dataset.Documents)
        {
            await AppendAsync(client, partition, document, cancellationToken);
        }
    }

    internal static async Task AppendAsync(KeyLoadClient client, PartitionRef partition, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        var receipt = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition,
            [new AppendEvents(EventsName, document.Id,
                [new EventData(BenchmarkDataset.EventId(document).ToString(), EventType, document.Json)],
                ExpectedStreamRevision.NoStream)]), cancellationToken));
        if (receipt.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw new ComparisonFailureException(WrongWriteProfile);
        }
    }

    internal static async Task<FoundEvent?> ReadAsync(KeyLoadClient client, PartitionRef partition,
        BenchmarkDocument document, CancellationToken cancellationToken)
    {
        const int BeforeFirstRevision = 0;
        const int NoItems = 0;
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        var page = KeyLoadClientResults.Success(await client.ReadStreamAsync(new(new(partition, EventsName, document.Id),
            AfterRevision: BeforeFirstRevision, Limit: RequiredEventReadCount), cancellationToken));
        if (page.Events.Length == NoItems && !page.HasMore)
        {
            return null;
        }

        if (page.Events.Length != SingleItemCount || page.HasMore)
        {
            throw new ComparisonFailureException(CardinalityFailure);
        }

        var found = page.Events[FirstElementIndex];
        return new(Guid.Parse(found.Data.EventId), checked((ulong)found.Revision), found.Data.PayloadJson);
    }
}
