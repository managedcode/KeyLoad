using KeyLoad.IntegrationTests.ChangeFeeds;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class EventCurrentReadAuthorityClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            var seeded = await EventCurrentReadAuthorityClusterSeed.CreateAsync(fixture, administrator, token);
            await ReadersAsync(fixture, seeded, async reader =>
            {
                (seeded.OriginalSdkCapture, seeded.OriginalMcpCapture) =
                    await CaptureAsync(administrator, reader, seeded, token);
                await EventCurrentReadAuthorityClusterPolicy.ConfigureAsync(administrator, seeded,
                    false, token);
                await RevokedAsync(administrator, reader, seeded, token);
            }, token);
            await SaveIdentityAsync(administrator, seeded, token);
            return seeded;
        }, token);
        await FeedLiveClusterCold.RestartAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            await IdentityAsync(administrator, state, token);
            await ReadersAsync(fixture, state, async reader =>
            {
                await RevokedAsync(administrator, reader, state, token);
                await EventCurrentReadAuthorityClusterPolicy.ConfigureAsync(administrator, state,
                    true, token);
                await FencedAsync(administrator, reader, state, ErrorCode.CursorExpired, token);
                (state.RepairedSdkCapture, state.RepairedMcpCapture) =
                    await CaptureAsync(administrator, reader, state, token);
                state.FreshCommand = new(Guid.NewGuid(), state.Scenario.Partition,
                    [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                        [EventCurrentReadAuthorityClusterExpected.Fourth], ExpectedStreamRevision.Exact(3), 1)]);
                state.FreshReceipt = await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.CommitAsync(state.FreshCommand, token));
                await SdkProblemBudgetPublicAssertions.ReceiptAsync(state.FreshReceipt,
                    state.FreshCommand, [new(EventCurrentReadAuthorityClusterExpected.AppendKind,
                        McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 4L)]);
                await Assert.That(state.FreshReceipt.Token.Position).IsGreaterThan(state.SeedReceipt.Token.Position);
                state.Updated = true;
                await EventCurrentReadAuthorityClusterCurrent.BaselineAsync(administrator, state, token);
                await RepairedAsync(administrator, reader, state, token);
                await ReplaysAsync(administrator, state, token);
            }, token);
            await SaveIdentityAsync(administrator, state, token);
            return true;
        }, token);
        await FeedLiveClusterCold.RestartAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            await IdentityAsync(administrator, state, token);
            await ReadersAsync(fixture, state, async reader =>
            {
                await FencedAsync(administrator, reader, state, ErrorCode.CursorExpired, token);
                await RepairedAsync(administrator, reader, state, token);
                await ReplaysAsync(administrator, state, token);
            }, token);
            return true;
        }, token);
    }

    private static async Task ReadersAsync(ClusterFixture fixture,
        EventCurrentReadAuthorityClusterState state, Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader => { await action(reader); return true; }, token);

    private static async Task<(StreamPage, StreamPage)> CaptureAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        var result = await EventCurrentReadAuthorityClusterCalls.ReadAsync(reader,
            new(state.Scenario.Stream, Limit: 1),
            () => EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { result.Sdk, result.Mcp })
        {
            await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
                [McpEventStreamScenario.InputEvents[0]], 1L, 3L,
                null, true);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.Take(1).ToArray(), page.Events.ToArray());
        }
        return result;
    }

    private static async Task RevokedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        await EventCurrentReadAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream), ErrorCode.PermissionDenied, token);
        foreach (var page in new[] { state.OriginalSdkCapture!, state.OriginalMcpCapture! })
        {
            await EventCurrentReadAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream, Cursor: page.Cursor), ErrorCode.PermissionDenied, token);
        }
    }

    private static async Task FencedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventCurrentReadAuthorityClusterState state, ErrorCode verifiedCode,
        CancellationToken token)
    {
        foreach (var page in new[] { state.OriginalSdkCapture!, state.OriginalMcpCapture! })
        {
            await EventCurrentReadAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream, Cursor: page.Cursor), verifiedCode, token);
        }
    }

    private static async Task RepairedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        foreach (var captured in new[] { state.RepairedSdkCapture!, state.RepairedMcpCapture! })
        {
            var result = await EventCurrentReadAuthorityClusterCalls.ReadAsync(reader,
                new(state.Scenario.Stream, Cursor: captured.Cursor),
                () => EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
            foreach (var page in new[] { result.Sdk, result.Mcp })
            {
                await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
                    McpEventStreamScenario.InputEvents.Skip(1).ToArray(), 2L, 3L,
                    captured.SnapshotCutPosition, false);
                await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.Skip(1).Take(2).ToArray(), page.Events.ToArray());
            }
        }
        var full = await EventCurrentReadAuthorityClusterCalls.ReadAsync(reader,
            new(state.Scenario.Stream),
            () => EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { full.Sdk, full.Mcp })
        {
            await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page,
                EventCurrentReadAuthorityClusterExpected.Data(true), 1L, 4L,
                null, false);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
        }
        var empty = await EventCurrentReadAuthorityClusterCalls.ReadAsync(reader,
            new(state.Scenario.Stream, AfterRevision: 4),
            () => EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { empty.Sdk, empty.Mcp })
        {
            await EventCurrentReadAuthorityClusterExpected.PageAsync(state, page, [], 5L, 4L,
                null, false);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
    }

    private static async Task ReplaysAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        Func<Task> observe = () => EventCurrentReadAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
        await EventCurrentReadAuthorityClusterCalls.ReplayAsync(administrator, state.Seed, state.SeedReceipt, observe, token);
        await EventCurrentReadAuthorityClusterCalls.ReplayAsync(administrator, state.FreshCommand!, state.FreshReceipt!, observe, token);
    }

    private static async Task SaveIdentityAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        state.BeforeNode = before.NodeId;
        state.BeforeIncarnation = before.Incarnation;
    }

    private static async Task IdentityAsync(RequestCqrsClusterCallers administrator,
        EventCurrentReadAuthorityClusterState state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.BeforeNode);
        await Assert.That(after.Incarnation).IsEqualTo(state.BeforeIncarnation);
        await Assert.That(after.RoutingReady).IsTrue();
    }
}
