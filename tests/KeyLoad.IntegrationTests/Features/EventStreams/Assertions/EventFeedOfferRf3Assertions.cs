using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedOfferRf3Assertions
{
    private const int TwoSources = 2;
    private const int First = 0;
    private const long FirstPosition = 1;
    private const string Payload = "{\"source\":\"native\"}";
    private const string Created = "Created";

    internal static async Task SourceAsync(EventSourcePage page, string eventId)
    {
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(FirstPosition, FirstPosition, FirstPosition));
        await Assert.That(page.Events.Length).IsEqualTo((int)FirstPosition);
        var value = page.Events[First];
        await Assert.That(value.Source).IsEqualTo(page.Source);
        await Assert.That(value.Position).IsEqualTo(FirstPosition);
        await Assert.That(value.EventSequence).IsEqualTo(FirstPosition);
        await SqlRf3Protocol.EqualAsync(new EventData(eventId, Created, Payload), value.Data);
        await Assert.That(value.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        await Assert.That(page.CutPosition).IsGreaterThan(0);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
    }

    internal static async Task OfferAsync(EventFeedControlRequest request, EventFeedControlResult actual,
        ImmutableArray<EventSourcePage> originalSources)
    {
        await Assert.That(actual.Version).IsEqualTo(EventFeedOfferProtocol.Version);
        await Assert.That(actual.PublicCommandId).IsEqualTo(request.CommandId);
        await Assert.That(actual.MapId).IsEqualTo(request.MapId);
        await Assert.That(actual.Revision).IsEqualTo(request.ExpectedRevision);
        await Assert.That(actual.CoverageGeneration).IsEqualTo(request.ExpectedCoverageGeneration);
        await Assert.That(actual.Released).IsFalse();
        await Assert.That(actual.Retained).IsTrue();
        await Assert.That(string.IsNullOrEmpty(actual.Cursor)).IsFalse();
        await Assert.That(string.IsNullOrEmpty(actual.OfferDigest)).IsFalse();
        await Assert.That(actual.Receipt.CommandId).IsNotEqualTo(request.CommandId);
        await Assert.That(actual.Receipt.Token.AtomicPartitionId).IsEqualTo(request.ControlPartition.AtomicPartitionId);
        await Assert.That(actual.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(actual.Receipt.Token.Position).IsGreaterThan(0);
        await Assert.That(actual.Pages.Length).IsEqualTo(TwoSources);
        foreach (var page in actual.Pages)
        {
            var original = originalSources.Single(source => source.Source == page.Source);
            await Assert.That(page.Head).IsEqualTo(original.Head);
            await SqlRf3Protocol.EqualAsync(original.Events, page.Events);
            await Assert.That(page.CutPosition >= original.CutPosition).IsTrue();
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        }
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, EventFeedControlRequest request,
        ErrorCode expected, CancellationToken token)
    {
        var sdk = await callers.Sdk.OfferEventFeedAsync(request, token);
        await Assert.That(sdk.IsSuccess).IsFalse();
        await Assert.That(sdk.Problem!.ErrorCode).IsEqualTo(expected.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(EventFeedOfferProtocol.Tool, request, token),
            expected, dispatched: true);
        var sql = await callers.Sdk.ExecuteSqlAsync(SqlRf3Protocol.Call(request.ControlPartition,
            EventFeedOfferProtocol.Tool, request), token);
        await Assert.That(sql.IsSuccess).IsFalse();
        await Assert.That(sql.Problem!.ErrorCode).IsEqualTo(expected.ToString());
    }
}
