using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedRefreshRf3Operations
{
    private const int Limit = 1;
    private const long AcknowledgedPosition = 1;
    private const int TwoSources = 2;
    private const string Created = "Created";
    private const string Payload = "{\"source\":\"native\"}";

    internal static async Task ConfigureAsync(TwoRf3MembershipWave wave, string credential,
        PartitionRef local, PartitionRef remote, string topic, CancellationToken token)
    {
        await ConfigureOneAsync(wave, credential, TwoRf3MembershipProtocol.Node1, local, topic, token).ConfigureAwait(false);
        await ConfigureOneAsync(wave, credential, TwoRf3MembershipProtocol.Node4, remote, topic, token).ConfigureAwait(false);
    }

    private static async Task ConfigureOneAsync(TwoRf3MembershipWave wave, string credential,
        string node, PartitionRef partition, string topic, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(topic, ResourceKind.Topic, partition.TransactionDomainId)), token));
    }

    internal static async Task PublishAsync(TwoRf3MembershipWave wave, string credential,
        string node, PartitionRef partition, string topic, string eventId, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        var command = new CommandRequest(Guid.NewGuid(), partition, [new PublishTopic(topic,
            [new(eventId, Created, Payload)])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
    }

    internal static Task<EventFeedControlResult> RoutesAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.RefreshEventFeedAsync(request, token));
                var official = (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(
                    await callers.Mcp.CallAsync(EventFeedRefreshProtocol.Tool, request, token))).Value;
                await SqlRf3Protocol.EqualAsync(original, official);
                await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<EventFeedControlResult>(callers.Sdk,
                    SqlRf3Protocol.Call(request.ControlPartition, EventFeedRefreshProtocol.Tool, request), token));
                return original;
            }, token);

    internal static Task RefusedAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var actual = await callers.Sdk.RefreshEventFeedAsync(request, token);
                await Assert.That(actual.IsSuccess).IsFalse();
                await Assert.That(actual.Problem!.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
                await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(EventFeedRefreshProtocol.Tool, request, token),
                    ErrorCode.Conflict, dispatched: true);
                var sql = await callers.Sdk.ExecuteSqlAsync(SqlRf3Protocol.Call(request.ControlPartition,
                    EventFeedRefreshProtocol.Tool, request), token);
                await Assert.That(sql.IsSuccess).IsFalse();
                await Assert.That(sql.Problem!.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
                return request;
            }, token);

    internal static async Task<EventFeedControlResult> RequireAcknowledgedAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest opened, EventFeedControlResult current, PartitionRef remote, CancellationToken token)
    {
        var request = opened with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Offer,
            ExpectedRevision = current.Revision,
            ExpectedCoverageGeneration = current.CoverageGeneration,
            Cursor = current.Cursor
        };
        var localPage = await ReadAcknowledgedAsync(wave, credential, TwoRf3MembershipProtocol.Node1,
            new(opened.ControlPartition, opened.Scope.Resource, EventSourceKind.Topic), token).ConfigureAwait(false);
        var remotePage = await ReadAcknowledgedAsync(wave, credential, TwoRf3MembershipProtocol.Node4,
            new(remote, opened.Scope.Resource, EventSourceKind.Topic), token).ConfigureAwait(false);
        var offered = await EventFeedOfferRf3Flow.RoutesAsync(wave, credential, request, token).ConfigureAwait(false);
        await EventFeedOfferRf3Assertions.OfferAsync(request, offered, [localPage, remotePage]).ConfigureAwait(false);
        await Assert.That(offered.Pages.Length).IsEqualTo(TwoSources);
        foreach (var page in offered.Pages)
        {
            var expected = page.Source.Partition == remote ? remotePage : localPage;
            await Assert.That(expected.Head).IsEqualTo(new EventSourceHead(AcknowledgedPosition, AcknowledgedPosition, AcknowledgedPosition));
            await Assert.That(expected.Events.IsEmpty).IsTrue();
            await Assert.That(page.Events.IsEmpty).IsTrue();
            await Assert.That(page.Source).IsEqualTo(expected.Source);
            await Assert.That(page.Head).IsEqualTo(expected.Head);
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.CutPosition >= expected.CutPosition).IsTrue();
            await Assert.That(string.IsNullOrEmpty(page.Cursor)).IsFalse();
        }
        await EventFeedTailRf3Flow.RunAsync(wave, credential, request, offered, token).ConfigureAwait(false);
        var acknowledge = request with
        {
            CommandId = Guid.NewGuid(),
            Action = EventFeedControlAction.Acknowledge,
            Cursor = offered.Cursor,
            OfferDigest = offered.OfferDigest
        };
        var acknowledged = await EventFeedSettlementRf3Flow.RoutesAsync(wave, credential, acknowledge, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Assertions.ResultAsync(acknowledge, acknowledged,
            checked(current.Revision + AcknowledgedPosition), released: false).ConfigureAwait(false);
        return acknowledged;
    }

    private static async Task<EventSourcePage> ReadAcknowledgedAsync(TwoRf3MembershipWave wave, string credential,
        string node, EventSourceRef source, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        return await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(
            new(source, AfterPosition: AcknowledgedPosition, Limit: Limit), token));
    }
}
