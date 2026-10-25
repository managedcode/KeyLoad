using KeyLoad.ClusterTests.ChangeFeeds;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class EventSelectiveAppendAuthorityClusterFlow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            var seeded = await EventSelectiveAppendAuthorityClusterSeed.CreateAsync(fixture, administrator, token);
            await ReaderAsync(fixture, seeded, async reader =>
            {
                await HealthyAsync(administrator, reader, seeded, token);
                await ReplayAsync(administrator, reader, seeded, seeded.Seed, seeded.SeedReceipt, token);
                await EventSelectiveAppendAuthorityClusterPolicy.ConfigureAsync(administrator, seeded, false, token);
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
            await ReaderAsync(fixture, state, async reader =>
            {
                await RevokedAsync(administrator, reader, state, token);
                await EventSelectiveAppendAuthorityClusterPolicy.ConfigureAsync(administrator, state, true, token);
                await FencedAsync(administrator, reader, state, token);
                state.FreshCommand = Fourth(state);
                state.FreshReceipt = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.Sdk.CommitAsync(state.FreshCommand, token));
                await SdkProblemBudgetPublicAssertions.ReceiptAsync(state.FreshReceipt,
                    state.FreshCommand, [new(EventSelectiveAppendAuthorityClusterExpected.AppendKind,
                        McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 4L)]);
                await Assert.That(state.FreshReceipt.Token.Position).IsGreaterThan(state.SeedReceipt.Token.Position);
                state.Updated = true;
                await EventSelectiveAppendAuthorityClusterCurrent.BaselineAsync(administrator, state, token);
                await HealthyAsync(administrator, reader, state, token);
                await ReplayAsync(administrator, reader, state, state.FreshCommand, state.FreshReceipt, token);
            }, token);
            await SaveIdentityAsync(administrator, state, token);
            return true;
        }, token);
        await FeedLiveClusterCold.RestartAsync(fixture, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            await IdentityAsync(administrator, state, token);
            await ReaderAsync(fixture, state, async reader =>
            {
                await FencedAsync(administrator, reader, state, token);
                await HealthyAsync(administrator, reader, state, token);
                await ReplayAsync(administrator, reader, state, state.FreshCommand!, state.FreshReceipt!, token);
            }, token);
            return true;
        }, token);
    }

    private static CommandRequest Fourth(EventSelectiveAppendAuthorityClusterState state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                [EventSelectiveAppendAuthorityClusterExpected.Fourth], ExpectedStreamRevision.Exact(3), 1)]);

    private static async Task ReaderAsync(ClusterFixture fixture, EventSelectiveAppendAuthorityClusterState state,
        Func<RequestCqrsClusterCallers, Task> action, CancellationToken token)
        => await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader => { await action(reader); return true; }, token);

    private static async Task RevokedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventSelectiveAppendAuthorityClusterState state, CancellationToken token)
    {
        await HealthyAsync(administrator, reader, state, token);
        await EventSelectiveAppendAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            Fourth(state), false, token);
        await EventSelectiveAppendAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            state.Seed, false, token);
    }

    private static async Task FencedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventSelectiveAppendAuthorityClusterState state, CancellationToken token)
    {
        await EventSelectiveAppendAuthorityClusterRefusals.RequireAsync(administrator, reader, state,
            state.Seed, true, token);
        await HealthyAsync(administrator, reader, state, token);
    }

    private static async Task ReplayAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventSelectiveAppendAuthorityClusterState state,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
        => await EventSelectiveAppendAuthorityClusterCalls.ReplayAsync(reader, command, receipt,
            () => EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);

    private static async Task HealthyAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, EventSelectiveAppendAuthorityClusterState state, CancellationToken token)
    {
        var full = await EventSelectiveAppendAuthorityClusterCalls.ReadAsync(reader,
            new(state.Scenario.Stream),
            () => EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { full.Sdk, full.Mcp })
        {
            await EventSelectiveAppendAuthorityClusterExpected.PageAsync(state, page,
                EventSelectiveAppendAuthorityClusterExpected.Data(state.Updated), 1L, state.Updated ? 4L : 3L,
                null, false);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
        }
        var empty = await EventSelectiveAppendAuthorityClusterCalls.ReadAsync(reader,
            new(state.Scenario.Stream, AfterRevision: state.Updated ? 4 : 3),
            () => EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { empty.Sdk, empty.Mcp })
        {
            await EventSelectiveAppendAuthorityClusterExpected.PageAsync(state, page, [],
                state.Updated ? 5L : 4L, state.Updated ? 4L : 3L, null, false);
            await SqlClusterProtocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
    }

    private static async Task SaveIdentityAsync(RequestCqrsClusterCallers administrator,
        EventSelectiveAppendAuthorityClusterState state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        state.BeforeNode = before.NodeId;
        state.BeforeIncarnation = before.Incarnation;
    }

    private static async Task IdentityAsync(RequestCqrsClusterCallers administrator,
        EventSelectiveAppendAuthorityClusterState state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.BeforeNode);
        await Assert.That(after.Incarnation).IsEqualTo(state.BeforeIncarnation);
        await Assert.That(after.RoutingReady).IsTrue();
    }
}
