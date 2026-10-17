using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Authorization
{
    private const string PrincipalId = "capacity-original-denied-admin";
    private const int SecretBytes = 32;

    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        TenantPlacementPlanResultV1 originalPlan, CancellationToken token)
        => await RequireAsync(wave, seed, state, originalPlan, pendingIntentRevocation: false, token).ConfigureAwait(false);

    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        TenantPlacementPlanResultV1 originalPlan, bool pendingIntentRevocation, CancellationToken token)
    {
        using var sourceHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        using var targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
        var sourceRoot = new KeyLoadClient(sourceHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var targetRoot = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
        var keyId = "capacity-key-" + Guid.NewGuid().ToString("N");
        var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes));
        var credential = new ApiKeyRecord(keyId, PrincipalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        await ConfigureAsync(sourceRoot, targetRoot, seed.Partition.TenantId, false, 1, credential, token);
        using var callerHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var caller = new KeyLoadClient(callerHttp, secret, IntegrationClientOptions.Execution());
        await using var official = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, secret, token);
        var query = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        await RequireDeniedPlanAsync(caller, official, query, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        var deniedId = Guid.NewGuid();
        var original = new PersistPlacementIntentRequest(Guid.NewGuid(), 0,
            originalPlan.SignedPlan, state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await caller.PersistPlacementIntentAsync(deniedId, original, token), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = deniedId,
                request = original
            }, token), ErrorCode.PermissionDenied, dispatched: true);
        await ConfigureAsync(sourceRoot, targetRoot, seed.Partition.TenantId, true, 2, credential, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await caller.PersistPlacementIntentAsync(deniedId, original, token), ErrorCode.PermissionDenied);
        var officialPlan = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request = query
            }, token))).Value;
        await TenantCapacityPlacementRf3Assertions.RequirePlanAsync(seed, state, officialPlan, token);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await caller.PlanTenantPlacementAsync(
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, [seed.Partition], state.Control), token));
        var repair = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, plan.SignedPlan, state.Control);
        var healthyId = Guid.NewGuid();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(
            await caller.PersistPlacementIntentAsync(healthyId, repair, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(healthy, plan.Plan, repair.IntentId);
        await SqlRf3Protocol.EqualAsync(healthy, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = healthyId,
                    request = repair
                }, token))).Value);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
        if (pendingIntentRevocation)
        {
            await TenantCapacityPlacementRf3Revocation.RunAsync(wave, seed, state, sourceRoot, targetRoot,
            secret, credential, healthy, healthyId, repair, token);
        }
    }

    private static async Task RequireDeniedPlanAsync(KeyLoadClient caller, McpOfficialClient official,
        TenantPlacementPlanRequest query, CancellationToken token)
    {
        const string McpDeniedDetail = "The authenticated principal is not permitted to perform this operation.";
        var sdk = await caller.PlanTenantPlacementAsync(query, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.PermissionDenied);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            KeyLoad.Clustering.GrainRoutingProtocol.AdministrationRequired), sdk.Problem!);
        var mcp = await official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
        {
            request = query
        }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(McpDeniedDetail);
    }

    internal static async Task ConfigureAsync(KeyLoadClient source, KeyLoadClient target, string tenant,
        bool administrator, long epoch, ApiKeyRecord key, CancellationToken token)
    {
        var principal = new PrincipalRecord(PrincipalId, tenant, [], [])
        {
            ClusterAdministrator = administrator,
            PolicyEpoch = epoch
        };
        foreach (var root in new[] { source, target })
        {
            var persisted = await McpCallerAssertions.SdkSuccessAsync(
                await root.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
            await Assert.That(persisted.ClusterAdministrator).IsEqualTo(administrator);
            await Assert.That(persisted.PolicyEpoch).IsEqualTo(epoch);
            _ = await McpCallerAssertions.SdkStatusAsync(
                await root.ConfigureApiKeyAsync(Guid.NewGuid(), key, token));
        }
    }
}
