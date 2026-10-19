using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class GuideLifecyclePublicValues
{
    internal static async Task DocumentAsync(RequestCqrsClusterCallers callers,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        var minimum = state.HealthyReceipt?.Token ?? state.SeedReceipt.Token;
        var expected = new DocumentResult(state.Scenario.Reference, state.HealthyReceipt is null ? 1L : 2L,
            state.HealthyReceipt is null ? GuideLifecyclePublicPreparation.InitialJson
                : GuideLifecyclePublicPreparation.HealthyJson, false, []);
        var actual = await PublicReadAssertions.DocumentAsync(await callers.Sdk.GetAsync(state.Scenario.Reference, minimum, token));
        await Assert.That(actual).IsNotNull();
        await SqlClusterProtocol.EqualAsync(expected, actual!);
        await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.RequiredDocumentAsync(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(state.Scenario.Reference, minimum), token))).Value);
    }

    internal static async Task CurrentAsync(RequestCqrsClusterCallers administrator,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        await DocumentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(state.Scenario.Partition, token)));
        await SqlClusterProtocol.EqualAsync(state.CurrentOutbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await administrator.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(state.Scenario.Partition), token))).Value);
    }

    internal static async Task SeedReplayAsync(RequestCqrsClusterCallers administrator,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(state.Seed, token)));
        await CurrentAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(state.SeedReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Seed, token))).Value);
        await CurrentAsync(administrator, state, token);
    }

    internal static async Task HealthyReplayAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, GuideLifecyclePublicState state, CancellationToken token)
    {
        await SeedReplayAsync(administrator, state, token);
        await SqlClusterProtocol.EqualAsync(state.HealthyReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(state.Healthy, token)));
        await CurrentAsync(administrator, state, token);
        await DocumentAsync(reader, state, token);
        await SqlClusterProtocol.EqualAsync(state.HealthyReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await reader.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Healthy, token))).Value);
        await CurrentAsync(administrator, state, token);
        await DocumentAsync(reader, state, token);
    }

    internal static async Task RepairReplayAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, GuideLifecyclePublicState state, CancellationToken token)
    {
        if (state.RepairOutcome is not { IsSuccess: true, Problem: null } original)
        {
            throw new InvalidOperationException("Original acknowledged credential repair is absent.");
        }
        await SqlClusterProtocol.EqualAsync(original, await McpCallerAssertions.SdkStatusAsync(
            await administrator.Sdk.ConfigureApiKeyAsync(state.RepairCommandId, state.Identity.Credential, token)));
        await CurrentAsync(administrator, state, token);
        await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
        var args = McpOfficialClient.Arguments(new ConfigureApiKeyRequest(state.Identity.Credential));
        args.Add(McpCallerProtocol.CommandId, state.RepairCommandId);
        _ = await McpCallerAssertions.StatusAsync(await administrator.Mcp.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.CredentialsConfigure, args, token));
        await CurrentAsync(administrator, state, token);
        await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
    }
}
