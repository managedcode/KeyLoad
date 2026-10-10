using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class LiveRepairedRf3Continuation
{
    internal static async Task RunAsync(FeedLiveRf3Connections owner,
        RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader, FeedLiveRf3Scenario scenario,
        ReadLiveQueryRequest obsolete, LiveQuerySnapshot repaired, CancellationToken token)
    {
        await Assert.That(repaired.ThroughSequence).IsEqualTo(FeedLiveRf3Protocol.DeletedSequence);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(FeedLiveRf3Protocol.Collection, FeedLiveRf3Protocol.First,
                FeedLiveRf3Protocol.FirstJson, FeedLiveRf3Protocol.DeletedRevision,
                Access: new(FeedLiveRf3Protocol.Owner), ExplicitReplacement: true)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await SqlRf3Protocol.EqualAsync(new[] { new MutationReceipt(FeedLiveRf3Protocol.RepairedMutationKind, FeedLiveRf3Protocol.Collection,
            FeedLiveRf3Protocol.First, FeedLiveRf3Protocol.RepairedRevision) }, receipt.Mutations.ToArray());
        var request = new ReadLiveQueryRequest(scenario.Query, repaired.Cursor);
        await LiveRepairedRf3Assertions.DeltaAsync(reader, scenario, request, receipt, token);
        await ReplayAsync(administrator, scenario, command, receipt, token);
        (administrator, reader) = await LiveSubscriptionRf3Cold.RestartAsync(owner, administrator, reader,
            scenario, token);
        await FeedLiveRf3NoEffects.RequireAsync(administrator, scenario,
            () => LiveTailRf3Assertions.DeniedAsync(reader, obsolete, ErrorCode.TokenInvalidated, token), token);
        await LiveRepairedRf3Assertions.DeltaAsync(reader, scenario, request, receipt, token);
        await ReplayAsync(administrator, scenario, command, receipt, token);
        await LiveRepairedRf3Assertions.FreshAsync(reader, scenario, receipt, token);
    }

    private static async Task ReplayAsync(RequestCqrsRf3Callers administrator, FeedLiveRf3Scenario scenario,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await Assert.That(before.Head.Tail).IsEqualTo(FeedLiveRf3Protocol.RepairedSequence);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(command, token));
        await Assert.That(NativeSerialization.Serialize(replay).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        await SqlRf3Protocol.EqualAsync(before, after);
    }
}
