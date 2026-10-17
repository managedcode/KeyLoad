using KeyLoad.IntegrationTests.Features.ChangeFeeds;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityRf3Flow
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async administrator =>
        {
            var seeded = await EventSelectiveAppendAuthorityRf3Seed.CreateAsync(fixture, administrator, token);
            await ReaderAsync(fixture, seeded, async reader =>
            {
                await HealthyAsync(administrator, reader, seeded, token);
                await ReplayAsync(administrator, reader, seeded, seeded.Seed, seeded.SeedReceipt, token);
                await EventSelectiveAppendAuthorityRf3Policy.ConfigureAsync(administrator, seeded, false, token);
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
            await ReaderAsync(fixture, state, async reader =>
            {
                await RevokedAsync(administrator, reader, state, token);
                await EventSelectiveAppendAuthorityRf3Policy.ConfigureAsync(administrator, state, true, token);
                await FencedAsync(administrator, reader, state, token);
                state.FreshCommand = Fourth(state);
                state.FreshReceipt = await McpCallerAssertions.SdkSuccessAsync(
                    await reader.Sdk.CommitAsync(state.FreshCommand, token));
                await SdkProblemBudgetPublicAssertions.ReceiptAsync(state.FreshReceipt,
                    state.FreshCommand, state.Placement,
                    [new(EventSelectiveAppendAuthorityRf3Expected.AppendKind,
                        McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 4L)]);
                await Assert.That(state.FreshReceipt.Token.Position).IsGreaterThan(state.SeedReceipt.Token.Position);
                state.Updated = true;
                await EventSelectiveAppendAuthorityRf3Current.BaselineAsync(administrator, state, token);
                await HealthyAsync(administrator, reader, state, token);
                await ReplayAsync(administrator, reader, state, state.FreshCommand, state.FreshReceipt, token);
            }, token);
            await SaveIdentityAsync(administrator, state, token);
            return true;
        }, token);
        await FeedLiveRf3Cold.RestartAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
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

    private static CommandRequest Fourth(EventSelectiveAppendAuthorityRf3State state)
        => new(Guid.NewGuid(), state.Scenario.Partition,
            [new AppendEvents(McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId,
                [EventSelectiveAppendAuthorityRf3Expected.Fourth], ExpectedStreamRevision.Exact(3), 1)]);

    private static async Task ReaderAsync(ClusterFixture fixture, EventSelectiveAppendAuthorityRf3State state,
        Func<RequestCqrsRf3Callers, Task> action, CancellationToken token)
        => await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
            state.Identity.Secret, async reader => { await action(reader); return true; }, token);

    private static async Task RevokedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        await HealthyAsync(administrator, reader, state, token);
        await EventSelectiveAppendAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            Fourth(state), false, token);
        await EventSelectiveAppendAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            state.Seed, false, token);
    }

    private static async Task FencedAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        await EventSelectiveAppendAuthorityRf3Refusals.RequireAsync(administrator, reader, state,
            state.Seed, true, token);
        await HealthyAsync(administrator, reader, state, token);
    }

    private static async Task ReplayAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventSelectiveAppendAuthorityRf3State state,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
        => await EventSelectiveAppendAuthorityRf3Calls.ReplayAsync(reader, command, receipt,
            () => EventSelectiveAppendAuthorityRf3Current.ObserveAsync(administrator, state, token), token);

    private static async Task HealthyAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers reader, EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        var full = await EventSelectiveAppendAuthorityRf3Calls.ReadAsync(reader,
            new(state.Scenario.Stream),
            () => EventSelectiveAppendAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { full.Sdk, full.Mcp })
        {
            await EventSelectiveAppendAuthorityRf3Expected.PageAsync(state, page,
                EventSelectiveAppendAuthorityRf3Expected.Data(state.Updated), 1L, state.Updated ? 4L : 3L,
                null, false);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Events.ToArray(), page.Events.ToArray());
        }
        var empty = await EventSelectiveAppendAuthorityRf3Calls.ReadAsync(reader,
            new(state.Scenario.Stream, AfterRevision: state.Updated ? 4 : 3),
            () => EventSelectiveAppendAuthorityRf3Current.ObserveAsync(administrator, state, token), token);
        foreach (var page in new[] { empty.Sdk, empty.Mcp })
        {
            await EventSelectiveAppendAuthorityRf3Expected.PageAsync(state, page, [],
                state.Updated ? 5L : 4L, state.Updated ? 4L : 3L, null, false);
            await SqlRf3Protocol.EqualAsync(state.CurrentPage.Head, page.Head);
        }
    }

    private static async Task SaveIdentityAsync(RequestCqrsRf3Callers administrator,
        EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        state.BeforeNode = before.NodeId;
        state.BeforeIncarnation = before.Incarnation;
    }

    private static async Task IdentityAsync(RequestCqrsRf3Callers administrator,
        EventSelectiveAppendAuthorityRf3State state, CancellationToken token)
    {
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.StatusAsync(token));
        await Assert.That(after.NodeId).IsEqualTo(state.BeforeNode);
        await Assert.That(after.Incarnation).IsEqualTo(state.BeforeIncarnation);
        await Assert.That(after.RoutingReady).IsTrue();
    }
}
