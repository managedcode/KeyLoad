using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class SubscriptionIndependentCheckpointR52Current
{
    internal static async Task CaptureAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var partition = state.Seed.Group.Source.Partition;
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.OutboxStatusAsync(partition, token));
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await official.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value, state.Outbox);
        var source = await McpCallerAssertions.SdkSuccessAsync(await admin.ReadEventSourceAsync(new(state.Seed.Group.Source), token));
        if (state.Source is not null)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(source.Events.Take(state.Source.Events.Length).ToArray(), state.Source.Events.ToArray());
        }
        state.Source = source;
        await SourceAsync(source, state);
        await SourceAsync((await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await official.CallAsync(McpCallerTools.EventsRead, new ReadEventSourceRequest(state.Seed.Group.Source), token))).Value, state);
    }

    internal static async Task RunAsync(SubscriptionIndependentCheckpointR52State state,
        KeyLoadClient sdk, McpOfficialClient mcp, KeyLoadClient admin, McpOfficialClient official, CancellationToken token)
    {
        var plain = new SubscriptionDefinition(state.Seed.Manager.Principal.Id)
        {
            Policy = new() { MaxWindow = SubscriptionFilterClusterProtocol.Window }
        };
        var primaryDefinition = state.PrimaryGeneration == 1 ? plain : state.Seed.Replacement.Definition;
        var primary = new SubscriptionInfo(state.Seed.Group, primaryDefinition, state.PrimaryGeneration,
            state.PrimaryGeneration, state.PrimaryCheckpoint, state.PrimaryIssued, state.Tail, state.PrimaryPaused, null);
        var independent = Expected(state, state.Seed.Independent);
        await StatusAsync(sdk, mcp, primary, token);
        await StatusAsync(sdk, mcp, independent, token);
        await SourceAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadEventSourceAsync(new(state.Seed.Group.Source), token)), state);
        await SourceAsync((await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await mcp.CallAsync(McpCallerTools.EventsRead, new ReadEventSourceRequest(state.Seed.Group.Source), token))).Value, state);
        var partition = state.Seed.Group.Source.Partition;
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.OutboxStatusAsync(partition, token)), state.Outbox);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await official.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value, state.Outbox);
    }

    internal static SubscriptionInfo Expected(SubscriptionIndependentCheckpointR52State state, SubscriptionRef group)
    {
        var plain = new SubscriptionDefinition(state.Seed.Manager.Principal.Id)
        {
            Policy = new() { MaxWindow = SubscriptionFilterClusterProtocol.Window }
        };
        return group == state.Seed.Independent
            ? new(group, state.IndependentDefinition, state.Generation, state.Generation,
                state.Checkpoint, state.Issued, state.Tail, state.Paused, null)
            : new(group, state.PrimaryGeneration == 1 ? plain : state.Seed.Replacement.Definition,
                state.PrimaryGeneration, state.PrimaryGeneration, state.PrimaryCheckpoint,
                state.PrimaryIssued, state.Tail, state.PrimaryPaused, null);
    }

    private static async Task StatusAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        SubscriptionInfo expected, CancellationToken token)
    {
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.SubscriptionStatusAsync(expected.Subscription, token)), expected);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await mcp.CallAsync(McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(expected.Subscription), token))).Value, expected);
    }

    private static async Task SourceAsync(EventSourcePage page, SubscriptionIndependentCheckpointR52State state)
    {
        var corpus = SubscriptionIndependentCheckpointR52Protocol.Corpus(state.Tail);
        await Assert.That(page.Head.TailPosition).IsEqualTo(state.Tail);
        await Assert.That(page.Events.Length).IsEqualTo(corpus.Length);
        await Assert.That(page.HasMore).IsFalse();
        for (var index = 0; index < corpus.Length; index++)
        {
            await Assert.That(page.Events[index].Position).IsEqualTo(index + 1L);
            await SubscriptionFilterClusterAssertions.EqualAsync(page.Events[index].Source, state.Seed.Group.Source);
            await SubscriptionFilterClusterAssertions.EqualAsync(page.Events[index].Data, corpus[index]);
        }
        if (state.Source is not null)
        {
            await SubscriptionFilterClusterAssertions.EqualAsync(page.Events, state.Source.Events);
            await SubscriptionFilterClusterAssertions.EqualAsync(page.Head, state.Source.Head);
        }
    }
}
