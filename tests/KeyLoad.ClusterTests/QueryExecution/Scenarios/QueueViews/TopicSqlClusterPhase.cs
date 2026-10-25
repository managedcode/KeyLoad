using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class TopicSqlClusterPhase
{
    internal static Task AuthorityAsync(ClusterFixture fixture, TopicSqlClusterState state, int route, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret, async reader =>
    {
        await TopicSqlClusterRoutes.DeniedAsync(reader, route, state, state.Sql(), ErrorCode.PermissionDenied, token);
        var principal = await GrantAsync(fixture, state, state.Identity.Principal, Capability.Query | Capability.TopicsRead, false, token);
        await TopicSqlClusterAssertions.RedactedAsync(await TopicSqlClusterRoutes.QueryAsync(reader, route, state, false, token));
        await TopicSqlClusterAssertions.RedactedAsync(await TopicSqlClusterRoutes.QueryAsync(reader, route, state, true, token));
        foreach (var sql in new[] { TopicSqlClusterProtocol.PrivatePredicate, TopicSqlClusterProtocol.PrivateOrder })
        {
            await TopicSqlClusterRoutes.DeniedAsync(reader, route, state, state.Sql(sql), ErrorCode.PermissionDenied, token);
        }
        principal = await GrantAsync(fixture, state, principal, Capability.Query | Capability.TopicsRead, true, token);
        await TopicSqlClusterAssertions.FullAsync(state, await TopicSqlClusterRoutes.QueryAsync(reader, route, state, false, token));
        await TopicSqlClusterAssertions.FullAsync(state, await TopicSqlClusterRoutes.QueryAsync(reader, route, state, true, token));
        await TopicSqlClusterRoutes.DeniedAsync(reader, route, state, state.Sql(TopicSqlClusterProtocol.StaleSql), ErrorCode.TokenInvalidated, token);
        principal = await GrantAsync(fixture, state, principal, Capability.TopicsRead, true, token);
        await TopicSqlClusterRoutes.DeniedAsync(reader, route, state, state.Sql(), ErrorCode.PermissionDenied, token);
        await GrantAsync(fixture, state, principal, Capability.Query | Capability.TopicsRead, true, token);
        await TopicSqlClusterAssertions.FullAsync(state, await TopicSqlClusterRoutes.QueryAsync(reader, route, state, false, token));
        return true;
    }, token);

    internal static async Task HealthyAsync(ClusterFixture fixture, TopicSqlClusterState state, int route, CancellationToken token)
    {
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret, async reader =>
        {
            await TopicSqlClusterAssertions.FullAsync(state, await TopicSqlClusterRoutes.QueryAsync(reader, route, state, false, token));
            await TopicSqlClusterAssertions.FullAsync(state, await TopicSqlClusterRoutes.QueryAsync(reader, route, state, true, token));
            var source = await TopicSqlClusterRoutes.RuntimeAsyncCore(reader, route, state, token);
            await Assert.That(source.Source).IsEqualTo(state.Source);
            await Assert.That(source.Head).IsEqualTo(state.Original.Head);
            await SqlClusterProtocol.EqualAsync(state.Original.Events, source.Events);
            await Assert.That(source.HasMore).IsFalse();
            await Assert.That(source.CutPosition).IsGreaterThan(0);
            return true;
        }, token);
        await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            var execution = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadEventSourceAsync(state.Read, token));
            await Assert.That(execution.Source).IsEqualTo(state.Source);
            await Assert.That(execution.Head).IsEqualTo(state.Original.Head);
            await SqlClusterProtocol.EqualAsync(state.Original.Events, execution.Events);
            var official = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await admin.Mcp.CallAsync(McpCallerTools.EventsRead, state.Read, token))).Value;
            await Assert.That(official.Head).IsEqualTo(state.Original.Head);
            await SqlClusterProtocol.EqualAsync(state.Original.Events, official.Events);
            var replay = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token));
            await SqlClusterProtocol.EqualAsync(state.Receipt, replay);
            var mcpReplay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value;
            await SqlClusterProtocol.EqualAsync(state.Receipt, mcpReplay);
            var q1 = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Command) with
            {
                CommandId = state.Command.CommandId
            };
            await SqlClusterProtocol.EqualAsync(state.Receipt, await SqlClusterProtocol.SdkAsync<CommitReceipt>(admin.Sdk, q1, token));
            await SqlClusterProtocol.EqualAsync(state.Receipt, await SqlClusterProtocol.McpAsync<CommitReceipt>(admin.Mcp, q1, token));
            var queue = await PublicReadAssertions.MessageAsync(await admin.Sdk.InspectAsync(state.Inspect, token));
            await SqlClusterProtocol.EqualAsync(state.OriginalQueue, queue);
            return true;
        }, token);
    }

    private static Task<PrincipalRecord> GrantAsync(ClusterFixture fixture, TopicSqlClusterState state,
        PrincipalRecord original, Capability capability, bool fields, CancellationToken token)
        => RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
    {
        var changed = original with
        {
            PolicyEpoch = original.PolicyEpoch + TopicSqlClusterProtocol.EpochIncrement,
            Grants = [new(state.Partition.DatabaseId, TopicSqlClusterProtocol.Topic, capability)],
            FieldGrants = fields ? [TopicSqlClusterProtocol.ReadGrant, TopicSqlClusterProtocol.UseGrant,
                TopicSqlClusterProtocol.HeaderRead, TopicSqlClusterProtocol.HeaderUse] : []
        };
        return await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), changed, token));
    }, token);
}
