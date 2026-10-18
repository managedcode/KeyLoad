using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveRepairedClusterContinuation
{
    internal static async Task RunAsync(FeedLiveClusterConnections owner,
        RequestCqrsClusterCallers administrator, RequestCqrsClusterCallers reader, FeedLiveClusterScenario scenario,
        ReadLiveQueryRequest obsolete, LiveQuerySnapshot repaired, CancellationToken token)
    {
        await Assert.That(repaired.ThroughSequence).IsEqualTo(FeedLiveClusterProtocol.DeletedSequence);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(FeedLiveClusterProtocol.Collection, FeedLiveClusterProtocol.First,
                FeedLiveClusterProtocol.FirstJson, FeedLiveClusterProtocol.DeletedRevision,
                Access: new(FeedLiveClusterProtocol.Owner), ExplicitReplacement: true)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await SqlClusterProtocol.EqualAsync(new[] { new MutationReceipt(FeedLiveClusterProtocol.RepairedMutationKind, FeedLiveClusterProtocol.Collection,
            FeedLiveClusterProtocol.First, FeedLiveClusterProtocol.RepairedRevision) }, receipt.Mutations.ToArray());
        var request = new ReadLiveQueryRequest(scenario.Query, repaired.Cursor);
        await LiveRepairedClusterAssertions.DeltaAsync(administrator, reader, scenario, request, receipt, token);
        await ReplayAsync(administrator, scenario, command, receipt, token);
        (administrator, reader) = await LiveSubscriptionClusterCold.RestartAsync(owner, administrator, reader,
            scenario, token);
        await FeedLiveClusterNoEffects.RequireAsync(administrator, scenario,
            FeedLiveClusterNoEffects.Rows(FeedLiveClusterProtocol.RepairedRevision, FeedLiveClusterProtocol.FirstJson), FeedLiveClusterProtocol.RepairedSequence,
            () => LiveTailClusterAssertions.DeniedAsync(reader, obsolete, ErrorCode.TokenInvalidated, token), token);
        await LiveRepairedClusterAssertions.DeltaAsync(administrator, reader, scenario, request, receipt, token);
        await ReplayAsync(administrator, scenario, command, receipt, token);
        await LiveRepairedClusterAssertions.FreshAsync(administrator, reader, scenario, receipt, token);
    }

    private static async Task ReplayAsync(RequestCqrsClusterCallers administrator, FeedLiveClusterScenario scenario,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(before.Head.Tail).IsEqualTo(FeedLiveClusterProtocol.RepairedSequence);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(NativeSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlClusterProtocol.EqualAsync(before, after);
    }
}
