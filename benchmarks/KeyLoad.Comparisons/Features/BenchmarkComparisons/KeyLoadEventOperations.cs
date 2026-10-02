using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadEventOperations
{
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
        var page = KeyLoadClientResults.Success(await client.ReadStreamAsync(new(new(partition, EventsName, document.Id),
            AfterRevision: 0, Limit: 2), cancellationToken));
        if (page.Events.Length == 0 && !page.HasMore)
        {
            return null;
        }

        if (page.Events.Length != 1 || page.HasMore)
        {
            throw new ComparisonFailureException(CardinalityFailure);
        }

        var found = page.Events[0];
        return new(Guid.Parse(found.Data.EventId), checked((ulong)found.Revision), found.Data.PayloadJson);
    }
}
