using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeAnnSettledRf3Flow
{
    private const Capability Grants = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;

    internal static async Task RunAsync(ClusterFixture fixture)
    {
        using var deadline = McpCallerDeadline.Create();
        var token = deadline.Token;
        var scenario = new NativeAnnMaintenanceRf3Scenario();
        var administrator = await CreateAdministratorAsync(fixture, scenario, token);
        var build = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            administrator.Secret, async caller =>
            {
                await scenario.SeedAsync(caller.Sdk, token);
                var request = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
                var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.MaintainAnnIndexAsync(request, token));
                await NativeAnnMaintenanceRf3Assertions.ResultAsync(result, request);
                await AnnPublicRf3Assertions.AllPathsAsync(caller.Sdk, caller.Mcp, scenario, request, empty: false, token);
                await NativeAnnSettledRf3Assertions.ReplayAsync(caller, scenario, request, result, token);
                await NativeAnnSettledRf3Assertions.RefuseAsync(caller,
                    request with
                    {
                        Mode = AnnMaintenanceMode.Restore
                    }, ErrorCode.Conflict, token);
                return (Request: request, Result: result, Batch: await NativeAnnSettledRf3Assertions.ReadAsync(caller.Sdk, request, token));
            }, token);
        await NativeAnnPublicColdOwners.RestartAsync(fixture, token);
        var restore = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            administrator.Secret, async caller =>
            {
                await NativeAnnSettledRf3Assertions.UnchangedAsync(build.Batch,
                    await NativeAnnSettledRf3Assertions.ReadAsync(caller.Sdk, build.Request, token));
                await NativeAnnSettledRf3Assertions.ReplayAsync(caller, scenario, build.Request, build.Result, token);
                var request = build.Request with
                {
                    CommandId = Guid.NewGuid(),
                    Mode = AnnMaintenanceMode.Restore
                };
                var result = (await McpCallerAssertions.SuccessAsync<AnnMaintenanceResult>(await caller.Mcp.CallAsync(
                    NativeAnnMaintenanceRf3Scenario.Tool, request, token))).Value;
                await NativeAnnMaintenanceRf3Assertions.ResultAsync(result, request);
                await AnnPublicRf3Assertions.AllPathsAsync(caller.Sdk, caller.Mcp, scenario, request, empty: false, token);
                await NativeAnnSettledRf3Assertions.RefuseAsync(caller, build.Request, ErrorCode.HistoryUnavailable, token);
                await NativeAnnSettledRf3Assertions.ReplayAsync(caller, scenario, request, result, token);
                return (Request: request, Result: result, Batch: await NativeAnnSettledRf3Assertions.ReadAsync(caller.Sdk, request, token));
            }, token);
        await NativeAnnPublicColdOwners.RestartAsync(fixture, token);
        await FreshNoOpAsync(fixture, administrator, scenario, restore.Request, restore.Result, restore.Batch, token);
    }

    private static async Task<McpPersistedIdentity> CreateAdministratorAsync(ClusterFixture fixture,
        NativeAnnMaintenanceRf3Scenario scenario, CancellationToken token)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            NativeAnnMaintenanceRf3Scenario.Collection, Grants, token);
        var principal = identity.Principal with
        {
            ClusterAdministrator = true,
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + McpCallerProtocol.EpochIncrement)
        };
        var actual = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async caller => await McpCallerAssertions.SdkSuccessAsync(
                await caller.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)), token);
        await NativeAnnSettledRf3Assertions.ExactAsync(principal, actual);
        return identity with
        {
            Principal = actual
        };
    }

    private static async Task FreshNoOpAsync(ClusterFixture fixture, McpPersistedIdentity administrator,
        NativeAnnMaintenanceRf3Scenario scenario, AnnMaintenanceRequest restore, AnnMaintenanceResult settled,
        ProjectionBatch originalBatch, CancellationToken token)
    {
        var original = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            administrator.Secret, async caller =>
            {
                await NativeAnnSettledRf3Assertions.UnchangedAsync(originalBatch,
                    await NativeAnnSettledRf3Assertions.ReadAsync(caller.Sdk, restore, token));
                await NativeAnnSettledRf3Assertions.ReplayAsync(caller, scenario, restore, settled, token);
                var request = restore with
                {
                    CommandId = Guid.NewGuid(),
                    Mode = AnnMaintenanceMode.Build
                };
                var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.MaintainAnnIndexAsync(request, token));
                await Assert.That(result.CommandId).IsEqualTo(request.CommandId);
                await Assert.That(result.Consumer).IsEqualTo(request.Consumer);
                await Assert.That(result.IndexGeneration).IsEqualTo(request.IndexGeneration);
                await Assert.That(result.Phase).IsEqualTo(AnnMaintenancePhase.Completed);
                await AnnPublicRf3Assertions.AllPathsAsync(caller.Sdk, caller.Mcp, scenario, request, empty: false, token);
                await NativeAnnSettledRf3Assertions.ReplayAsync(caller, scenario, request, result, token);
                return request;
            }, token);
        await RefuseChangedAuthorityAsync(fixture, administrator, scenario, original, token);
    }

    private static async Task RefuseChangedAuthorityAsync(ClusterFixture fixture, McpPersistedIdentity administrator,
        NativeAnnMaintenanceRf3Scenario scenario, AnnMaintenanceRequest original, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async healthy =>
            {
                var before = await NativeAnnSettledRf3Assertions.ReadAsync(healthy.Sdk, original, token);
                var changed = administrator.Principal with
                {
                    ClusterAdministrator = false,
                    PolicyEpoch = checked(administrator.Principal.PolicyEpoch + McpCallerProtocol.EpochIncrement)
                };
                var actual = await McpCallerAssertions.SdkSuccessAsync(await healthy.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), changed, token));
                await NativeAnnSettledRf3Assertions.ExactAsync(changed, actual);
                _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
                    administrator.Secret, async denied =>
                    {
                        await NativeAnnSettledRf3Assertions.DeniedAsync(denied, original, ErrorCode.PermissionDenied, token);
                        return true;
                    }, token);
                await NativeAnnSettledRf3Assertions.UnchangedAsync(before,
                    await NativeAnnSettledRf3Assertions.ReadAsync(healthy.Sdk, original, token));
                await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(healthy.Sdk, healthy.Mcp, scenario, token);
                return true;
            }, token);
    }
}
