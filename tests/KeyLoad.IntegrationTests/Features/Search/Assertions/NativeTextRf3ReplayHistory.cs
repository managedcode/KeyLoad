using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextRf3ReplayHistory
{
    internal static async Task<ChangeFeedPage> CaptureAsync(KeyLoadClient administrator, PartitionRef partition,
        CancellationToken token)
    {
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(partition, NativeTextRf3Scenario.Collection), token));
        await Assert.That(page.HasMore).IsFalse();
        return page;
    }

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
