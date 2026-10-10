using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class SubscriptionFilterRf3Refusals
{
    internal static async Task RunAsync(KeyLoadClient manager, McpOfficialClient mcp,
        SubscriptionFilterRf3Seed seed, CancellationToken token)
    {
        var request = seed.Replacement;
        var before = await McpCallerAssertions.SdkSuccessAsync(await manager.SubscriptionStatusAsync(seed.Group, token));
        var same = request with { CommandId = Guid.NewGuid(), Definition = before.Definition };
        await SubscriptionFilterRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await manager.ConfigureSubscriptionAsync(same, token)), before);
        await SubscriptionFilterRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, same, token))).Value, before);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp, request with
        { CommandId = Guid.NewGuid(), ExpectedGeneration = SubscriptionFilterRf3Protocol.Updated }, ErrorCode.RevisionConflict, token);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp, request with
        { CommandId = Guid.NewGuid(), Start = SubscriptionStart.FromNow }, ErrorCode.Validation, token);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp, request with
        { CommandId = Guid.NewGuid(), Cursor = SubscriptionFilterRf3Protocol.InvalidCursor }, ErrorCode.Validation, token);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp, request with
        { CommandId = Guid.NewGuid(), ExpectedGeneration = null }, ErrorCode.Conflict, token);
        await SubscriptionFilterRf3Assertions.RefusedAsync(manager, mcp, request with
        {
            CommandId = Guid.NewGuid(),
            Definition = request.Definition with
            { EventTypes = [SubscriptionFilterRf3Protocol.Changed, SubscriptionFilterRf3Protocol.Changed] }
        }, ErrorCode.Validation, token);
        var absent = request with { CommandId = Guid.NewGuid(), Subscription = request.Subscription with { GroupId = SubscriptionFilterRf3Protocol.MissingGroup } };
        var result = await manager.ConfigureSubscriptionAsync(absent, token);
        await Assert.That(result.Problem!.ErrorCode).IsEqualTo(ErrorCode.NotFound.ToString());
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, absent, token), ErrorCode.NotFound, true);
        await SubscriptionFilterRf3Assertions.StatusAsync(manager, mcp, seed.Group, SubscriptionFilterRf3Protocol.Initial, SubscriptionFilterRf3Protocol.Gap, true, token);
    }
}
