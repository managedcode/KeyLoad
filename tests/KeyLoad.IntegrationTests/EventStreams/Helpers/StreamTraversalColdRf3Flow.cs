using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class StreamTraversalColdRf3Flow
{
    private static readonly EventData Fourth = McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.Empty] with
    {
        EventId = "traversal-delivered",
        PayloadJson = "{\"status\":\"delivered\"}",
        HeadersJson = "{\"source\":\"delivery\"}",
        CausationId = "traversal-original-cause"
    };

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, callers => BeforeColdAsync(callers, fixture, deadline.Token), deadline.Token);
        await FeedLiveRf3Cold.RestartAsync(fixture, deadline.Token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
        {
            var after = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(deadline.Token));
            await Assert.That(after.NodeId).IsEqualTo(state.Before.NodeId);
            await Assert.That(after.Incarnation).IsEqualTo(state.Before.Incarnation);
            await Assert.That(after.Voters).IsEqualTo(state.Before.Voters);
            await Assert.That(after.RoutingReady).IsTrue();
            await ResumeAsync(callers, state.Scenario, state.First, state.Backward, state.Fourth, deadline.Token);
            await SqlRf3Protocol.EqualAsync(state.Original,
                await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Append, deadline.Token)));
            await SqlRf3Protocol.EqualAsync(state.Fourth,
                await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.FourthCommand, deadline.Token)));
            return state;
        }, deadline.Token);
    }

    private static async Task<StreamTraversalRf3State> BeforeColdAsync(RequestCqrsRf3Callers callers,
        ClusterFixture fixture, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var append = scenario.AppendCommand(Guid.NewGuid());
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(append, token));
        var first = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(
            new(scenario.Stream, Limit: StreamTraversalTestProtocol.First), token));
        var backward = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(
            new(scenario.Stream, Limit: StreamTraversalTestProtocol.First, Direction: StreamReadDirection.Backward), token));
        await RequireFirstAsync(scenario, first, StreamTraversalTestProtocol.First, McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.Empty], original);
        await RequireFirstAsync(scenario, backward, StreamTraversalTestProtocol.Third, McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.Second], original);
        var fourthCommand = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                [Fourth], ExpectedStreamRevision.Exact(StreamTraversalTestProtocol.Third), StreamTraversalTestProtocol.First)]);
        var fourth = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(fourthCommand, token));
        await Assert.That(fourth.Token.Position).IsGreaterThan(original.Token.Position);
        await StreamTraversalRf3Assertions.DeniedAsync(callers, scenario,
            new(scenario.Stream, Cursor: first.Cursor, Direction: StreamReadDirection.Backward),
            ErrorCode.CursorExpired, token);
        await StreamTraversalRf3Assertions.DeniedAsync(callers, scenario,
            new(scenario.Stream, Cursor: first.Cursor + "x"), ErrorCode.TokenInvalidated, token);
        await ResumeAsync(callers, scenario, first, backward, fourth, token);

        var before = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.StatusAsync(token));
        return new(scenario, append, original, fourthCommand, fourth, first, backward, before);
    }

    private static async Task RequireFirstAsync(McpEventStreamScenario scenario, StreamPage actual,
        long revision, EventData expected, CommitReceipt original)
    {
        await Assert.That(actual.HasMore).IsTrue();
        await Assert.That(actual.Cursor).IsNotNull();
        await Assert.That(actual.SnapshotCutPosition).IsEqualTo(actual.CutPosition);
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(original.Token.Position);
        await StreamTraversalRf3Assertions.PageAsync(scenario,
            actual, [revision], [expected], StreamTraversalTestProtocol.Third,
            actual.SnapshotCutPosition, original.Token.Position, more: true, cursor: actual.Cursor);
    }

    private static async Task ResumeAsync(RequestCqrsRf3Callers callers, McpEventStreamScenario scenario,
        StreamPage first, StreamPage backward, CommitReceipt fourth, CancellationToken token)
    {
        await StreamTraversalRf3Assertions.RoutesAsync(callers, scenario, new(scenario.Stream, Cursor: first.Cursor),
            [StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third], [McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.First], McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.Second]],
            StreamTraversalTestProtocol.Third, first.SnapshotCutPosition, fourth.Token.Position, token);
        await StreamTraversalRf3Assertions.RoutesAsync(callers, scenario,
            new(scenario.Stream, Direction: StreamReadDirection.Backward, Cursor: backward.Cursor),
            [StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.First], [McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.First], McpEventStreamScenario.ExpectedEvents[StreamTraversalTestProtocol.Empty]],
            StreamTraversalTestProtocol.Third, backward.SnapshotCutPosition, fourth.Token.Position, token);
        var fresh = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadStreamAsync(new(scenario.Stream), token));
        await StreamTraversalRf3Assertions.RoutesAsync(callers, scenario, new(scenario.Stream), [StreamTraversalTestProtocol.First, StreamTraversalTestProtocol.Second, StreamTraversalTestProtocol.Third, StreamTraversalTestProtocol.Fourth],
            [.. McpEventStreamScenario.ExpectedEvents, Fourth], StreamTraversalTestProtocol.Fourth, fresh.CutPosition, fourth.Token.Position, token);
    }
}
