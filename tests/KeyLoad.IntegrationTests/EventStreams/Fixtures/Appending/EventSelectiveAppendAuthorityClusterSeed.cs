using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventSelectiveAppendAuthorityClusterSeed
{
    internal static async Task<EventSelectiveAppendAuthorityClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers administrator, CancellationToken token)
    {
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            McpEventStreamTokens.StreamSet, Capability.EventsRead | Capability.EventsAppend, token);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var receipt = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App,
            McpCallerProtocol.Node2, identity.Secret, async reader =>
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(command, token)), token);
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, command, [new(EventSelectiveAppendAuthorityClusterExpected.AppendKind,
                McpEventStreamTokens.StreamSet, McpEventStreamTokens.StreamId, 3L)]);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(1L);
        var page = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadStreamAsync(new(scenario.Stream), token));
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(scenario.Partition, token));
        var state = new EventSelectiveAppendAuthorityClusterState(scenario, identity, command, receipt, page, outbox);
        await EventSelectiveAppendAuthorityClusterExpected.PageAsync(state, page,
            EventSelectiveAppendAuthorityClusterExpected.Data(false), 1L, 3L, null, false);
        await EventSelectiveAppendAuthorityClusterCurrent.ObserveAsync(administrator, state, token);
        return state;
    }
}
