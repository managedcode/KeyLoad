using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventCurrentReadAuthorityRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            var seeded = await EventCurrentReadAuthorityRf3Seed.CreateAsync(fixture, administrator, token);
            await ReadersAsync(fixture, seeded, async reader =>
            {
                (seeded.OriginalSdkCapture, seeded.OriginalMcpCapture) =
                    await CaptureAsync(administrator, reader, seeded, token);
                await EventCurrentReadAuthorityRf3Policy.ConfigureAsync(administrator, seeded,
                    false, token);
                await RevokedAsync(administrator, reader, seeded, token);
            }, token);
            await SaveIdentityAsync(administrator, seeded, token);
            return seeded;
        }, token);
        await FeedLiveRf3Cold.RestartAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            await IdentityAsync(administrator, state, token);
            await ReadersAsync(fixture, state, async reader =>
            {
                await RevokedAsync(administrator, reader, state, token);
                await EventCurrentReadAuthorityRf3Policy.ConfigureAsync(administrator, state,
                    true, token);
                await FencedAsync(administrator, reader, state, ErrorCode.CursorExpired, token);
                (state.RepairedSdkCapture, state.RepairedMcpCapture) =
                    await CaptureAsync(administrator, reader, state, token);
                state.FreshCommand = new(Guid.NewGuid(), state.Scenario.Partition,
                    [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                        [EventCurrentReadAuthorityRf3Expected.Fourth], ExpectedStreamRevision.Exact(3), 1)]);
                state.FreshReceipt = await McpCallerAssertions.SdkSuccessAsync(
                    await administrator.Sdk.CommitAsync(state.FreshCommand, token));
                await SdkProblemBudgetPublicAssertions.ReceiptAsync(state.FreshReceipt,
                    state.FreshCommand, state.Placement,
                    [new(EventCurrentReadAuthorityRf3Expected.AppendKind,
                        McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 4L)]);
                await Assert.That(state.FreshReceipt.Token.Position).IsGreaterThan(state.SeedReceipt.Token.Position);
                state.Updated = true;
                await EventCurrentReadAuthorityRf3Current.BaselineAsync(administrator, state, token);
                await RepairedAsync(administrator, reader, state, token);
                await ReplaysAsync(administrator, state, token);
            }, token);
            await SaveIdentityAsync(administrator, state, token);
            return true;
        }, token);
        await FeedLiveRf3Cold.RestartAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
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
        EventCurrentReadAuthorityRf3State state, Func<RequestCqrsRf3Callers, Task> action, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader => { await action(reader); return true; }, token);

    private static async Task<(StreamPage, StreamPage)> CaptureAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        var result = await EventCurrentReadAuthorityRf3Calls.ReadAsync(reader,
            new(state.Scenario.Stream, Limit: 1),
            () => EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { result.Sdk, result.Mcp })
        {
            await EventCurrentReadAuthorityRf3Expected.PageAsync(state, page,
                [McpEventStreamScenario.ExpectedEvents[0]], 1L, 3L,
                null, true);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.Take(1).ToArray(), page.Events.ToArray());
        }
        return result;
    }

    private static async Task RevokedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        await EventCurrentReadAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream), ErrorCode.PermissionDenied, token);
        foreach (var page in new[] { state.OriginalSdkCapture!, state.OriginalMcpCapture! })
        {
            await EventCurrentReadAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream, Cursor: page.Cursor), ErrorCode.PermissionDenied, token);
        }
    }

    private static async Task FencedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventCurrentReadAuthorityRf3State state, ErrorCode verifiedCode,
        CancellationToken token)
    {
        foreach (var page in new[] { state.OriginalSdkCapture!, state.OriginalMcpCapture! })
        {
            await EventCurrentReadAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            new(state.Scenario.Stream, Cursor: page.Cursor), verifiedCode, token);
        }
    }

    private static async Task RepairedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        foreach (var captured in new[] { state.RepairedSdkCapture!, state.RepairedMcpCapture! })
        {
            var result = await EventCurrentReadAuthorityRf3Calls.ReadAsync(reader,
                new(state.Scenario.Stream, Cursor: captured.Cursor),
                () => EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
            foreach (var page in new[] { result.Sdk, result.Mcp })
            {
                await EventCurrentReadAuthorityRf3Expected.PageAsync(state, page,
                    McpEventStreamScenario.ExpectedEvents.Skip(1).ToArray(), 2L, 3L,
                    captured.SnapshotCutPosition, false);
                await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.Skip(1).Take(2).ToArray(), page.Events.ToArray());
            }
        }
        var full = await EventCurrentReadAuthorityRf3Calls.ReadAsync(reader,
            new(state.Scenario.Stream),
            () => EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { full.Sdk, full.Mcp })
        {
            await EventCurrentReadAuthorityRf3Expected.PageAsync(state, page,
                EventCurrentReadAuthorityRf3Expected.Data(true), 1L, 4L,
                null, false);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
        }
        var empty = await EventCurrentReadAuthorityRf3Calls.ReadAsync(reader,
            new(state.Scenario.Stream, AfterRevision: 4),
            () => EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { empty.Sdk, empty.Mcp })
        {
            await EventCurrentReadAuthorityRf3Expected.PageAsync(state, page, [], 5L, 4L,
                null, false);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
    }

    private static async Task ReplaysAsync(RequestCqrsRf3Callers administrator,
        EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        Func<Task> observe = () => EventCurrentReadAuthorityRf3Current.ObserveAsync(administrator, state, token);
        await EventCurrentReadAuthorityRf3Calls.ReplayAsync(administrator, state.Seed, state.SeedReceipt, observe, token);
        await EventCurrentReadAuthorityRf3Calls.ReplayAsync(administrator, state.FreshCommand!, state.FreshReceipt!, observe, token);
    }

    private static async Task SaveIdentityAsync(RequestCqrsRf3Callers administrator,
        EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        state.BeforeNode = before.NodeId;
        state.BeforeIncarnation = before.Incarnation;
    }

    private static async Task IdentityAsync(RequestCqrsRf3Callers administrator,
        EventCurrentReadAuthorityRf3State state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.BeforeNode);
        await Assert.That(after.Incarnation).IsEqualTo(state.BeforeIncarnation);
        await Assert.That(after.RoutingReady).IsTrue();
    }
}
