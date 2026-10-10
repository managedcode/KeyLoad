using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class StreamCatchupColdRf3Flow
{
    internal const long CatchupRevision = 3;
    internal const long TailRevision = 4;
    internal static readonly EventData TailEvent = McpEventStreamScenario.ExpectedEvents[0] with
    {
        EventId = "catchup-delivered",
        PayloadJson = "{\"status\":\"delivered\"}",
        HeadersJson = "{\"source\":\"delivery\"}",
        CausationId = "catchup-original-cause"
    };

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var (scenario, command, original, originalSourceCursor, append, tail, before) =
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                fixture.AdminKey, async callers =>
        {
            var scenario = await McpEventStreamScenario.CreateAsync(fixture, deadline.Token);
            var command = scenario.AppendCommand(Guid.NewGuid());
            var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, deadline.Token));
            await StreamCatchupRf3Assertions.HistoryAsync(callers, scenario, CatchupRevision,
                original.Token.Position, deadline.Token);
            var originalSourceCursor = await StreamCatchupSourceCursorAssertions.CaptureAsync(
                callers, scenario, original, deadline.Token);
            var request = scenario.ReadRequest(CatchupRevision, McpEventStreamTokens.EmptyTailLimit);
            var empty = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(request, deadline.Token));
            await StreamCatchupRf3Assertions.PageAsync(scenario, empty, CatchupRevision, [], original.Token.Position);
            var (append, tail) = await AppendAtTailAsync(callers, scenario, request,
                original, deadline.Token);
            await StreamCatchupRf3Assertions.DeniedThenHealthyAsync(callers, scenario, tail, deadline.Token);
            await StreamCatchupSourceCursorAssertions.RequireAsync(callers, originalSourceCursor, tail, deadline.Token);
            var before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));

            return (scenario, command, original, originalSourceCursor, append, tail, before);
        }, deadline.Token);
        await FeedLiveRf3Cold.RestartAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
        {
            var after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
            await RequireSameOwnerAsync(before, after);
            await StreamCatchupRf3Assertions.ContinuationAsync(callers, scenario, tail, deadline.Token);
            await StreamCatchupSourceCursorAssertions.RequireAsync(callers, originalSourceCursor, tail, deadline.Token);
            await StreamCatchupRf3Assertions.HistoryAsync(callers, scenario, TailRevision,
                tail.Token.Position, deadline.Token);
            await SqlRf3Protocol.EqualAsync(original,
                await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, deadline.Token)));
            await SqlRf3Protocol.EqualAsync(tail,
                (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
                    McpCallerTools.DocumentsCommit, append, deadline.Token))).Value);
            await StreamCatchupRf3Assertions.ContinuationAsync(callers, scenario, tail, deadline.Token);
            return tail;
        }, deadline.Token);
    }

    private static async Task<(CommandRequest Command, CommitReceipt Receipt)> AppendAtTailAsync(
        RequestCqrsRf3Callers callers, McpEventStreamScenario scenario, ReadStreamRequest request,
        CommitReceipt original, CancellationToken token)
    {
        var append = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                [TailEvent], ExpectedStreamRevision.Exact(CatchupRevision), McpEventStreamTokens.StreamGeneration)]);
        var read = callers.Sdk.ReadStreamAsync(request, token);
        var write = callers.Sdk.CommitAsync(append, token);
        var joined = Task.WhenAll(read, write);
        await joined.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (joined.Exception is { } failures)
        { throw failures; }
        await joined;
        var raced = await McpCallerAssertions.SdkSuccessAsync(await read);
        var tail = await McpCallerAssertions.SdkSuccessAsync(await write);
        await Assert.That(tail.Token.Position).IsGreaterThan(original.Token.Position);
        var included = raced.CutPosition >= tail.Token.Position;
        await StreamCatchupRf3Assertions.PageAsync(scenario, raced,
            included ? TailRevision : CatchupRevision, included ? [TailEvent] : [], original.Token.Position);
        return (append, tail);
    }

    private static async Task RequireSameOwnerAsync(NodeStatus before, NodeStatus after)
    {
        await Assert.That(after.NodeId).IsEqualTo(before.NodeId);
        await Assert.That(after.Incarnation).IsEqualTo(before.Incarnation);
        await Assert.That(after.Voters).IsEqualTo(before.Voters);
        await Assert.That(after.RoutingReady).IsTrue();
        await Assert.That(after.ReadGeneration).IsGreaterThanOrEqualTo(before.ReadGeneration);
    }
}
