using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class GuideLifecyclePublicTrial
{
    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var state = await WarmAsync(fixture, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        await RepairAfterRevokedColdAsync(fixture, state, token);
        await CredentialAuthorityCold.RestartAsync(fixture, token);
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
            return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node3,
                state.Identity.Secret, async reader =>
            {
                await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
                await GuideLifecyclePublicValues.DocumentAsync(reader, state, token);
                await GuideLifecyclePublicValues.RepairReplayAsync(root, reader, state, token);
                await GuideLifecyclePublicValues.HealthyReplayAsync(root, reader, state, token);
                await GuideLifecyclePublicControls.RefusalsAndCancellationAsync(root, reader, state, token);
                return true;
            }, token);
        }, token);
    }

    private static Task<GuideLifecyclePublicState> WarmAsync(ClusterFixture fixture, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            var state = await GuideLifecyclePublicPreparation.CreateAsync(fixture, root, token);
            await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Secret, async reader =>
            {
                await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
                await GuideLifecyclePublicValues.DocumentAsync(reader, state, token);
                await GuideLifecyclePublicControls.RefusalsAndCancellationAsync(root, reader, state, token);
                await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
                    state.Identity.Credential with
                    {
                        Revoked = true
                    }, token));
                await GuideLifecyclePublicControls.RevokedAsync(root, reader, state, token);
                return true;
            }, token);
            return state;
        }, token);

    private static async Task RepairAfterRevokedColdAsync(ClusterFixture fixture,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1, fixture.AdminKey, async root =>
        {
            await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
            await RevokedFreshAsync(fixture, root, state, token);
            state.RepairOutcome = await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(
                state.RepairCommandId, state.Identity.Credential, token));
            await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
            return await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                state.Identity.Secret, async reader =>
            {
                await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
                await GuideLifecyclePublicValues.DocumentAsync(reader, state, token);
                await GuideLifecyclePublicValues.RepairReplayAsync(root, reader, state, token);
                state.HealthyReceipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(state.Healthy, token));
                await SdkProblemBudgetPublicAssertions.ReceiptAsync(state.HealthyReceipt, state.Healthy, state.Placement,
                    [new("putDocument", state.Scenario.Reference.Collection, state.Scenario.Reference.Id, 2L)]);
                state.CurrentOutbox = await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
                await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
                await GuideLifecyclePublicValues.DocumentAsync(reader, state, token);
                await GuideLifecyclePublicValues.HealthyReplayAsync(root, reader, state, token);
                return true;
            }, token);
        }, token);
    }

    private static async Task RevokedFreshAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var sdk = new KeyLoadClient(http, state.Identity.Secret, IntegrationClientOptions.Execution());
        await GuideLifecyclePublicControls.SdkRevokedAsync(sdk, state, token);
        await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
        var rejected = await Assert.ThrowsAsync<HttpRequestException>(() => RequestCqrsRf3Callers.RunOwnedAsync<bool>(
            fixture.App, McpCallerProtocol.Node3, state.Identity.Secret,
            _ => throw new InvalidOperationException("A revoked credential unexpectedly admitted a fresh official client."), token));
        await GuideLifecyclePublicControls.UnauthorizedAsync(rejected, state);
        await GuideLifecyclePublicValues.CurrentAsync(root, state, token);
        await GuideLifecyclePublicValues.SeedReplayAsync(root, state, token);
    }
}
