using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class TopicSqlRf3Phase
{
    internal static Task AuthorityAsync(ClusterFixture fixture, TopicSqlRf3State state, int route, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret, async reader =>
    {
        await TopicSqlRf3Routes.DeniedAsync(reader, route, state, state.Sql(), ErrorCode.PermissionDenied, token);
        var principal = await GrantAsync(fixture, state, state.Identity.Principal, Capability.Query | Capability.TopicsRead, false, token);
        await TopicSqlRf3Assertions.RedactedAsync(await TopicSqlRf3Routes.QueryAsync(reader, route, state, false, token));
        await TopicSqlRf3Assertions.RedactedAsync(await TopicSqlRf3Routes.QueryAsync(reader, route, state, true, token));
        foreach (var sql in new[] { TopicSqlRf3Protocol.PrivatePredicate, TopicSqlRf3Protocol.PrivateOrder })
        { await TopicSqlRf3Routes.DeniedAsync(reader, route, state, state.Sql(sql), ErrorCode.PermissionDenied, token); }
        principal = await GrantAsync(fixture, state, principal, Capability.Query | Capability.TopicsRead, true, token);
        await TopicSqlRf3Assertions.FullAsync(state, await TopicSqlRf3Routes.QueryAsync(reader, route, state, false, token));
        await TopicSqlRf3Assertions.FullAsync(state, await TopicSqlRf3Routes.QueryAsync(reader, route, state, true, token));
        await TopicSqlRf3Routes.DeniedAsync(reader, route, state, state.Sql(TopicSqlRf3Protocol.StaleSql), ErrorCode.TokenInvalidated, token);
        principal = await GrantAsync(fixture, state, principal, Capability.TopicsRead, true, token);
        await TopicSqlRf3Routes.DeniedAsync(reader, route, state, state.Sql(), ErrorCode.PermissionDenied, token);
        await GrantAsync(fixture, state, principal, Capability.Query | Capability.TopicsRead, true, token);
        await TopicSqlRf3Assertions.FullAsync(state, await TopicSqlRf3Routes.QueryAsync(reader, route, state, false, token));
        return true;
    }, token);

    internal static async Task HealthyAsync(ClusterFixture fixture, TopicSqlRf3State state, int route, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, state.Identity.Secret, async reader =>
        {
            await TopicSqlRf3Assertions.FullAsync(state, await TopicSqlRf3Routes.QueryAsync(reader, route, state, false, token));
            await TopicSqlRf3Assertions.FullAsync(state, await TopicSqlRf3Routes.QueryAsync(reader, route, state, true, token));
            var source = await TopicSqlRf3Routes.NativeAsync(reader, route, state, token);
            await Assert.That(source.Source).IsEqualTo(state.Source);
            await Assert.That(source.Head).IsEqualTo(state.Original.Head);
            await SqlRf3Protocol.EqualAsync(state.Original.Events, source.Events);
            await Assert.That(source.HasMore).IsFalse();
            await Assert.That(source.CutPosition).IsGreaterThan(0);
            return true;
        }, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
        {
            var native = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadEventSourceAsync(state.Read, token));
            await Assert.That(native.Source).IsEqualTo(state.Source);
            await Assert.That(native.Head).IsEqualTo(state.Original.Head);
            await SqlRf3Protocol.EqualAsync(state.Original.Events, native.Events);
            var official = (await McpCallerAssertions.SuccessAsync<EventSourcePage>(await admin.Mcp.CallAsync(McpCallerTools.EventsRead, state.Read, token))).Value;
            await Assert.That(official.Head).IsEqualTo(state.Original.Head);
            await SqlRf3Protocol.EqualAsync(state.Original.Events, official.Events);
            var replay = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(state.Command, token));
            await SqlRf3Protocol.EqualAsync(state.Receipt, replay);
            var mcpReplay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await admin.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Command, token))).Value;
            await SqlRf3Protocol.EqualAsync(state.Receipt, mcpReplay);
            var q1 = SqlRf3Protocol.Call(state.Partition, McpCallerTools.DocumentsCommit, state.Command, state.Command.CommandId);
            await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.SdkAsync<CommitReceipt>(admin.Sdk, q1, token));
            await SqlRf3Protocol.EqualAsync(state.Receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(admin.Mcp, q1, token));
            var queue = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.InspectAsync(state.Inspect, token));
            await SqlRf3Protocol.EqualAsync(state.OriginalQueue, queue);
            return true;
        }, token);
    }

    private static Task<PrincipalRecord> GrantAsync(ClusterFixture fixture, TopicSqlRf3State state,
        PrincipalRecord original, Capability capability, bool fields, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3, fixture.AdminKey, async admin =>
    {
        var changed = original with
        {
            PolicyEpoch = original.PolicyEpoch + TopicSqlRf3Protocol.EpochIncrement,
            Grants = [new(state.Partition.DatabaseId, TopicSqlRf3Protocol.Topic, capability)],
            FieldGrants = fields ? [TopicSqlRf3Protocol.ReadGrant, TopicSqlRf3Protocol.UseGrant,
                TopicSqlRf3Protocol.HeaderRead, TopicSqlRf3Protocol.HeaderUse] : []
        };
        return await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), changed, token));
    }, token);
}
