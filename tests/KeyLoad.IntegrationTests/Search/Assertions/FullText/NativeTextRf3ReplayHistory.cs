using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRf3ReplayHistory
{
    private const long FirstSequence = 1;
    private const long InitialRevision = 1;
    private const long SeedMutationCount = 6;
    private const int FirstIndex = 0;

    internal static async Task<ChangeFeedPage> CaptureAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(partition, NativeTextRf3Scenario.Collection), token));
        await Assert.That(page.HasMore).IsFalse();
        return page;
    }

    internal static async Task RequireSeedAsync(NativeTextRf3Scenario scenario, ChangeFeedPage page)
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
            await SqlRf3Protocol.EqualAsync(scenario.SeedToken, change.Commit);
            await Assert.That(change.Reference).IsEqualTo(expected[index].Reference);
            await Assert.That(change.Revision).IsEqualTo(InitialRevision);
            await Assert.That(change.Before).IsNull();
            await Assert.That(change.Deleted).IsFalse();
            await SqlRf3Protocol.EqualAsync(expected[index], change.After);
            await Assert.That(change.CommittedAt.Offset).IsEqualTo(TimeSpan.Zero);
            await Assert.That(change.CommittedAt).IsNotEqualTo(default(DateTimeOffset));
            await Assert.That(change.CommittedAt).IsEqualTo(page.Changes[FirstIndex].CommittedAt);
        }
    }

    internal static async Task RequireDocumentsAsync(KeyLoadClient administrator,
        NativeTextRf3Scenario scenario, CancellationToken token)
    {
        foreach (var expected in SeedDocuments(scenario))
        {
            var response = await McpCallerAssertions.SdkSuccessAsync(
                await administrator.GetAsync(expected.Reference, token));
            await SqlRf3Protocol.EqualAsync(expected, response.Document);
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

    private static DocumentResult[] SeedDocuments(NativeTextRf3Scenario scenario)
        =>
        [
            new(new(scenario.Partition, NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.FirstId),
                InitialRevision, NativeTextRf3Scenario.Document("Київ needle needle", NativeTextRf3Scenario.OwnerA), false, []),
            new(new(scenario.Partition, NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.SecondId),
                InitialRevision, NativeTextRf3Scenario.Document("needle plain", NativeTextRf3Scenario.OwnerB), false, []),
            new(new(scenario.Partition, NativeTextRf3Scenario.Collection, NativeTextRf3Scenario.ThirdId),
                InitialRevision, NativeTextRf3Scenario.Document("other", NativeTextRf3Scenario.OwnerB), false, [])
        ];

    internal static async Task RequireAsync(KeyLoadClient administrator, PartitionRef partition,
        ChangeFeedPage before, CancellationToken token)
    {
        var after = await CaptureAsync(administrator, partition, token);
        await SqlRf3Protocol.EqualAsync(before.Changes, after.Changes);
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
            new(partition, NativeTextRf3Scenario.Collection, page.Cursor), token));
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
