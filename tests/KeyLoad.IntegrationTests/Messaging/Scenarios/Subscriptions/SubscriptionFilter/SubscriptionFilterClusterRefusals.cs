using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class SubscriptionFilterClusterRefusals
{
    internal static async Task RunAsync(KeyLoadClient manager, McpOfficialClient mcp,
        SubscriptionFilterClusterSeed seed, CancellationToken token)
    {
        var request = seed.Replacement;
        var before = await McpCallerAssertions.SdkSuccessAsync(await manager.SubscriptionStatusAsync(seed.Group, token));
        var same = request with
        {
            CommandId = Guid.NewGuid(),
            Definition = before.Definition
        };
        await SubscriptionFilterClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.ConfigureSubscriptionAsync(same, token)), before);
        await SubscriptionFilterClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, same, token))).Value, before);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            ExpectedGeneration = SubscriptionFilterClusterProtocol.Updated
        }, ErrorCode.RevisionConflict, token);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            Start = SubscriptionStart.FromNow
        }, ErrorCode.Validation, token);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            Cursor = SubscriptionFilterClusterProtocol.InvalidCursor
        }, ErrorCode.Validation, token);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            ExpectedGeneration = null
        }, ErrorCode.Conflict, token);
        await SubscriptionFilterClusterAssertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            Definition = request.Definition with
            {
                EventTypes = [SubscriptionFilterClusterProtocol.Changed, SubscriptionFilterClusterProtocol.Changed]
            }
        }, ErrorCode.Validation, token);
        var absent = request with
        {
            CommandId = Guid.NewGuid(),
            Subscription = request.Subscription with
            {
                GroupId = SubscriptionFilterClusterProtocol.MissingGroup
            }
        };
        var result = await manager.ConfigureSubscriptionAsync(absent, token);
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.NotFound.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, absent, token), ErrorCode.NotFound, true);
        await SubscriptionFilterClusterAssertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterClusterProtocol.Initial, SubscriptionFilterClusterProtocol.Gap, true, token);
    }
}
