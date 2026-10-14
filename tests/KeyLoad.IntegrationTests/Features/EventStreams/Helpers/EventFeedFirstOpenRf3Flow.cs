using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedFirstOpenRf3Flow
{
    private const string Tenant = "protected-parent-tenant";
    private const string Database = "feed-database";
    private const string Domain = "feed-domain";
    private const string Topic = "feed-topic";
    private const string First = "feed-source-first";
    private const string Second = "feed-source-second";
    private const int Version = 1;
    private const int Limit = 1;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        var credential = await PartitionMovementPublicParentRf3Administrator.PersistAsync(wave, token).ConfigureAwait(false);
        var control = new PartitionRef(Tenant, Database, Domain, Guid.NewGuid().ToString("N"));
        var remote = new PartitionRef(Tenant, Database, Domain, Guid.NewGuid().ToString("N"));
        await SeedAsync(wave, credential, TwoRf3MembershipProtocol.Node1, control, First, token).ConfigureAwait(false);
        await SeedAsync(wave, credential, TwoRf3MembershipProtocol.Node4, remote, Second, token).ConfigureAwait(false);
        var request = new EventFeedControlRequest
        {
            Version = Version,
            CommandId = Guid.NewGuid(),
            ControlPartition = control,
            MapId = Guid.NewGuid(),
            Action = EventFeedControlAction.Open,
            Scope = new() { Domain = Domain, Resource = Topic, Kind = EventSourceKind.Topic },
            Start = EventFeedStartPolicy.FromBeginning,
            Limit = Limit
        };
        await EventFeedFirstCleanupRf3Flow.RunAsync(wave, credential, request, token).ConfigureAwait(false);
        var original = await RoutesAsync(wave, credential, request, token).ConfigureAwait(false);
        await EventFeedFirstOpenRf3Assertions.OpenAsync(request, original).ConfigureAwait(false);
        var offer = await EventFeedOfferRf3Flow.RunAsync(wave, credential, request, original,
            control, remote, First, Second, token).ConfigureAwait(false);
        await EventFeedTailRf3Flow.RunAsync(wave, credential, offer.Request, offer.Result, token).ConfigureAwait(false);
        await EventFeedReferencedOutcomeRf3Flow.RunAsync(wave, credential, offer.Request, offer.Result, token).ConfigureAwait(false);
        await SourceAuthorityAsync(wave, credential, request, original, offer, token).ConfigureAwait(false);
        var retiredRequest = request;
        var retiredOpen = original;
        var retiredOffer = offer;
        var retiredRelease = await EventFeedRepairedSourceRf3Settlement.ReleaseAsync(wave, credential, offer, token).ConfigureAwait(false);
        request = request with
        {
            CommandId = Guid.NewGuid(),
            MapId = Guid.NewGuid()
        };
        original = await RoutesAsync(wave, credential, request, token).ConfigureAwait(false);
        await EventFeedFirstOpenRf3Assertions.OpenAsync(request, original).ConfigureAwait(false);
        offer = await EventFeedOfferRf3Flow.RunAsync(wave, credential, request, original,
            control, remote, First, Second, token).ConfigureAwait(false);
        var lateReceipt = await SeedAsync(wave, credential, TwoRf3MembershipProtocol.Node4, remote, Second + "-late", token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await RoutesAsync(wave, credential, request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(offer.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offer.Request, token).ConfigureAwait(false));
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        await wave.RestartJoinedAsync(token).ConfigureAwait(false);
        await wave.WaitForSixHealthyAsync(token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await RoutesAsync(wave, credential, request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(offer.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offer.Request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(retiredOpen, await RoutesAsync(wave, credential, retiredRequest, token).ConfigureAwait(false));
        await EventFeedRepairedSourceRf3Settlement.ReplayAsync(wave, credential, retiredOffer, retiredRelease, token).ConfigureAwait(false);
        await EventFeedTailRf3Flow.RunAsync(wave, credential, offer.Request, offer.Result, token).ConfigureAwait(false);
        var latest = await EventFeedTailRf3PairFlow.RunAsync(wave, credential, offer.Request, offer.Result,
            remote, lateReceipt, Second + "-late", Second + "-third", token).ConfigureAwait(false);
        var refresh = await EventFeedRefreshRf3Flow.RunAsync(wave, credential, request, remote, token).ConfigureAwait(false);
        await EventFeedSettlementRf3Flow.RunAsync(wave, credential, latest.Request, latest.Result, token).ConfigureAwait(false);
        await refresh.RequireAsync(wave, credential, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(offer.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offer.Request, token).ConfigureAwait(false));
    }

    private static async Task<CommitReceipt> SeedAsync(TwoRf3MembershipWave wave, string credential, string node,
        PartitionRef partition, string eventId, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, node);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution());
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(Topic, ResourceKind.Topic, Domain)), token));
        var command = new CommandRequest(Guid.NewGuid(), partition,
            [new PublishTopic(Topic, [new(eventId, "Created", "{\"source\":\"native\"}")])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        return receipt;
    }

    internal static Task<EventFeedControlResult> RoutesAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OpenEventFeedAsync(request, token));
                var official = (await McpCallerAssertions.SuccessAsync<EventFeedControlResult>(
                    await callers.Mcp.CallAsync(EventFeedOpenProtocol.Tool, request, token))).Value;
                await SqlRf3Protocol.EqualAsync(original, official);
                await SqlRf3Protocol.EqualAsync(original, await SqlRf3Protocol.SdkAsync<EventFeedControlResult>(callers.Sdk,
                    SqlRf3Protocol.Call(request.ControlPartition, EventFeedOpenProtocol.Tool, request), token));
                return original;
            }, token);

    private static async Task SourceAuthorityAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest request, EventFeedControlResult original,
        (EventFeedControlRequest Request, EventFeedControlResult Result) offer, CancellationToken token)
    {
        using var targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var administrator = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        await EventFeedFirstOpenRf3Authority.RevokeAsync(administrator, token).ConfigureAwait(false);
        await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node1, credential,
            async callers =>
            {
                await EventFeedFirstOpenRf3Assertions.DeniedAsync(callers, request, token).ConfigureAwait(false);
                await EventFeedOfferRf3Assertions.DeniedAsync(callers, offer.Request, ErrorCode.PermissionDenied, token).ConfigureAwait(false);
                return request;
            }, token).ConfigureAwait(false);
        await EventFeedTailRf3Refusal.RunAsync(wave, credential, offer.Request, offer.Result, token).ConfigureAwait(false);
        await EventFeedFirstOpenRf3Authority.RepairAsync(administrator, token).ConfigureAwait(false);
        await SqlRf3Protocol.EqualAsync(original, await RoutesAsync(wave, credential, request, token).ConfigureAwait(false));
        await SqlRf3Protocol.EqualAsync(offer.Result, await EventFeedOfferRf3Flow.RoutesAsync(
            wave, credential, offer.Request, token).ConfigureAwait(false));
        await EventFeedTailRf3Flow.RunAsync(wave, credential, offer.Request, offer.Result, token).ConfigureAwait(false);
    }
}
