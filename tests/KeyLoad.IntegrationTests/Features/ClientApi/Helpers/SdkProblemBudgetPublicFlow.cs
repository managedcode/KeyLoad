using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class SdkProblemBudgetPublicFlow
{
    internal static async Task RefusalsAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicCallers caller, SdkProblemBudgetPublicState state, CancellationToken token)
    {
        await SdkProblemBudgetPublicAssertions.FailureAsync(await caller.Minimum.GetAsync(state.Forbidden, token),
            ErrorCode.OwnershipLost, true);
        await AfterRefusalAsync(administrator, state, token);
        await SdkProblemBudgetPublicAssertions.FailureAsync(await caller.Minimum.CommitAsync(state.Denied, token),
            ErrorCode.UnknownWriteOutcome, true);
        await AfterRefusalAsync(administrator, state, token);
        await SdkProblemBudgetPublicAssertions.FailureAsync(await caller.Normal.GetAsync(state.Forbidden, token),
            ErrorCode.PermissionDenied, false);
        await AfterRefusalAsync(administrator, state, token);
        await SdkProblemBudgetPublicAssertions.FailureAsync(await caller.Normal.CommitAsync(state.Denied, token),
            ErrorCode.PermissionDenied, false);
        await AfterRefusalAsync(administrator, state, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(state.Forbidden), token), ErrorCode.PermissionDenied, true);
        await AfterRefusalAsync(administrator, state, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.Denied, token), ErrorCode.PermissionDenied, true);
        await AfterRefusalAsync(administrator, state, token);
        await ReplaySeedAsync(administrator, state, token);
        await SdkProblemBudgetPublicAssertions.HealthyAsync(caller, state, token);
    }

    internal static async Task<SdkProblemBudgetPublicState> PublishHealthyAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicCallers caller, SdkProblemBudgetPublicState state, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Minimum.CommitAsync(state.Healthy, token));
        await SdkProblemBudgetPublicAssertions.ReceiptAsync(receipt, state.Healthy, state.Placement,
            [new("putDocument", McpDocumentProtocol.Collection, SdkProblemBudgetPublicPreparation.Entity, 2L)]);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.OutboxStatusAsync(state.Seed.Partition, token));
        var current = state with { HealthyReceipt = receipt, Outbox = outbox };
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, current, token);
        await ReplayHealthyAsync(administrator, caller, current, token);
        return current;
    }

    private static async Task AfterRefusalAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicState state, CancellationToken token)
    {
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        await ReplaySeedAsync(administrator, state, token);
    }

    private static async Task ReplaySeedAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicState state, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(state.Seed, token)));
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.SeedReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
    }

    internal static async Task ReplayHealthyAsync(RequestCqrsRf3Callers administrator,
        SdkProblemBudgetPublicCallers caller, SdkProblemBudgetPublicState state, CancellationToken token)
    {
        await ReplaySeedAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.HealthyReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Minimum.CommitAsync(state.Healthy, token)));
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.HealthyReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Normal.CommitAsync(state.Healthy, token)));
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        await SqlRf3Protocol.EqualAsync(state.HealthyReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await caller.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Healthy, token))).Value);
        await SdkProblemBudgetPublicAssertions.CurrentAsync(administrator, state, token);
        await SdkProblemBudgetPublicAssertions.HealthyAsync(caller, state, token);
    }
}
