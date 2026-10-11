using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3NextOffer
{
    private const int OneEvent = 1;
    private const int EmptyEvents = 0;
    private const long OriginalGeneration = 1;
    private const long RevisionStep = 1;
    private const long LocalAcknowledgedPosition = 1;
    private const long FirstAvailable = 1;
    private const string Created = "Created";
    private const string Payload = "{\"source\":\"native\"}";

    internal static async Task<(EventFeedControlRequest Request, EventFeedControlResult Result)> RunAsync(
        TwoRf3MembershipWave wave, string credential, EventFeedControlRequest offered,
        EventFeedControlResult original, PartitionRef remote, string expectedEventId, CommitReceipt? originalSourceReceipt, bool publish,
        CancellationToken token)
    {
        var acknowledge = offered with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Acknowledge,
            ExpectedRevision = original.Revision,
            ExpectedCoverageGeneration = original.CoverageGeneration,
            Cursor = original.Cursor,
            OfferDigest = original.OfferDigest
        };
        var acknowledged = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(acknowledge, acknowledged,
            checked(original.Revision + RevisionStep), released: false).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offered, token).ConfigureAwait(false));
        var sourceReceipt = publish ? await PublishAsync(wave, credential, remote,
            offered.Scope.Resource, expectedEventId, token).ConfigureAwait(false) : originalSourceReceipt;
        if (sourceReceipt is null || sourceReceipt.Token.AtomicPartitionId != remote.AtomicPartitionId)
        { throw new InvalidOperationException("The two-offer flow requires its actual original source receipt."); }
        var pages = ImmutableArray.CreateBuilder<EventSourcePage>(original.Pages.Length);
        foreach (var prior in original.Pages)
        {
            var remoteSource = prior.Source.Partition == remote;
            var after = remoteSource ? prior.Events.Single().Position : LocalAcknowledgedPosition;
            var page = await ReadAsync(wave, credential, remoteSource ? TwoRf3MembershipProtocol.Node4
                : TwoRf3MembershipProtocol.Node1, new(prior.Source, after, OneEvent), token).ConfigureAwait(false);
            await RequireSourceAsync(page, after, remoteSource, expectedEventId).ConfigureAwait(false);
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(remoteSource
                ? sourceReceipt.Token.Position : prior.CutPosition);
            pages.Add(page);
        }
        var next = offered with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Offer,
            ExpectedRevision = acknowledged.Revision,
            ExpectedCoverageGeneration = acknowledged.CoverageGeneration,
            Cursor = acknowledged.Cursor,
            OfferDigest = null
        };
        var actual = await EventFeedOfferRf3Flow.RoutesAsync(wave, credential, next, token).ConfigureAwait(false);
        await EventFeedOfferRf3Assertions.OfferAsync(next, actual, pages.ToImmutable()).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(acknowledged,
            await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false));
        return (next, actual);
    }

    private static async Task RequireSourceAsync(EventSourcePage page, long after, bool remote, string eventId)
    {
        var tail = remote ? checked(after + OneEvent) : after;
        await Assert.That(page.Head).IsEqualTo(new EventSourceHead(tail, FirstAvailable, OriginalGeneration));
        await Assert.That(page.Events.Length).IsEqualTo(remote ? OneEvent : EmptyEvents);
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.CutPosition).IsGreaterThan(0);
        await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        if (!remote)
        { return; }
        var actual = page.Events[EmptyEvents];
        await Assert.That(actual.Source).IsEqualTo(page.Source);
        await Assert.That(actual.Position).IsEqualTo(tail);
        await Assert.That(actual.EventSequence).IsEqualTo(tail);
        await SqlRf3Protocol.EqualAsync(new EventData(eventId, Created, Payload), actual.Data);
        await Assert.That(actual.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
    }

    private static Task<EventSourcePage> ReadAsync(TwoRf3MembershipWave wave, string credential, string node,
        ReadEventSourceRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, credential,
            async callers => await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.ReadEventSourceAsync(request, token).ConfigureAwait(false)), token);

    private static async Task<CommitReceipt> PublishAsync(TwoRf3MembershipWave wave, string credential, PartitionRef remote,
        string topic, string eventId, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        var command = new CommandRequest(Guid.NewGuid(), remote, [new PublishTopic(topic, [new(eventId, Created, Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token).ConfigureAwait(false));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(remote.AtomicPartitionId);
        return receipt;
    }
}
