using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Search;

internal static class TextClusterReplayHistory
{
    private const long FirstSequence = 1;
    private const long InitialRevision = 1;
    private const long SeedMutationCount = 6;
    private const int FirstIndex = 0;

    internal static async Task<ChangeFeedPage> CaptureAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(partition, TextClusterScenario.Collection), token));
        await Assert.That(page.HasMore).IsFalse();
        return page;
    }

    internal static async Task RequireSeedAsync(TextClusterScenario scenario, ChangeFeedPage page)
    {
        var expected = SeedDocuments(scenario);
        await Assert.That(page.Changes.Length).IsEqualTo(expected.Length);
        await Assert.That(page.ThroughSequence).IsEqualTo(SeedMutationCount);
        await Assert.That(page.Tail).IsEqualTo(SeedMutationCount);
        await Assert.That(page.FirstAvailable).IsEqualTo(FirstSequence);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        for (var index = FirstIndex; index < expected.Length; index++)
        {
            var change = page.Changes[index];
            await Assert.That(change.Sequence).IsEqualTo(FirstSequence + index);
            await SqlClusterProtocol.EqualAsync(scenario.SeedToken, change.Commit);
            await Assert.That(change.Reference).IsEqualTo(expected[index].Reference);
            await Assert.That(change.Revision).IsEqualTo(InitialRevision);
            await Assert.That(change.Before).IsNull();
            await Assert.That(change.Deleted).IsFalse();
            await SqlClusterProtocol.EqualAsync(expected[index], change.After);
            await Assert.That(change.CommittedAt.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
            await Assert.That(change.CommittedAt).IsEqualTo(page.Changes[FirstIndex].CommittedAt);
        }
    }

    internal static async Task RequireDocumentsAsync(KeyLoadClient administrator,
        TextClusterScenario scenario, CancellationToken token)
    {
        foreach (var expected in SeedDocuments(scenario))
        {
            var response = await McpCallerAssertions.SdkSuccessAsync(
                await administrator.GetAsync(new(scenario.Partition, TextClusterScenario.Collection, expected.Reference.Id), token));
            await SqlClusterProtocol.EqualAsync(expected, response.Document);
        }
    }

    internal static async Task RequireSeedOutboxAsync(OutboxStatus actual)
    {
        await Assert.That(actual.Head.Tail).IsEqualTo(SeedMutationCount);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(FirstSequence);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(SeedMutationCount);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(0L);
        await Assert.That(actual.Consumers.IsEmpty).IsTrue();
    }

    private static DocumentResult[] SeedDocuments(TextClusterScenario scenario)
        =>
        [
            new(scenario.StoredReference(TextClusterScenario.FirstId),
                InitialRevision, TextClusterScenario.Document("Київ needle needle", TextClusterScenario.OwnerA), false, []),
            new(scenario.StoredReference(TextClusterScenario.SecondId),
                InitialRevision, TextClusterScenario.Document("needle plain", TextClusterScenario.OwnerB), false, []),
            new(scenario.StoredReference(TextClusterScenario.ThirdId),
                InitialRevision, TextClusterScenario.Document("other", TextClusterScenario.OwnerB), false, [])
        ];

    internal static async Task RequireAsync(KeyLoadClient administrator, PartitionRef partition,
        ChangeFeedPage before, CancellationToken token)
    {
        var after = await CaptureAsync(administrator, partition, token);
        await SqlClusterProtocol.EqualAsync(before.Changes, after.Changes);
        await MetadataAsync(before, after);
        await Assert.That(after.CutPosition).IsGreaterThanOrEqualTo(before.CutPosition);
        await CursorAsync(administrator, partition, before, token);
        await CursorAsync(administrator, partition, after, token);
    }

    private static async Task CursorAsync(KeyLoadClient administrator, PartitionRef partition,
        ChangeFeedPage page, CancellationToken token)
    {
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        var continued = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(partition, TextClusterScenario.Collection, page.Cursor), token));
        await Assert.That(continued.Changes.IsEmpty).IsTrue();
        await MetadataAsync(page, continued);
        await Assert.That(continued.CutPosition).IsGreaterThanOrEqualTo(page.CutPosition);
    }

    private static async Task MetadataAsync(ChangeFeedPage before, ChangeFeedPage after)
    {
        await Assert.That(after.ThroughSequence).IsEqualTo(before.ThroughSequence);
        await Assert.That(after.Tail).IsEqualTo(before.Tail);
        await Assert.That(after.FirstAvailable).IsEqualTo(before.FirstAvailable);
        await Assert.That(after.HasMore).IsEqualTo(before.HasMore);
    }
}
