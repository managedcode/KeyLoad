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
        var deniedId = Guid.NewGuid();
        var original = new PersistPlacementIntentRequest(Guid.NewGuid(), 0,
            originalPlan.SignedPlan, state.Control);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await caller.PersistPlacementIntentAsync(deniedId, original, token), ErrorCode.PermissionDenied);
        await McpCallerAssertions.ErrorAsync(await official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new { commandId = deniedId, request = original }, token), ErrorCode.PermissionDenied, dispatched: true);
        await ConfigureAsync(sourceRoot, targetRoot, seed.Partition.TenantId, true, 2, credential, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(
            await caller.PersistPlacementIntentAsync(deniedId, original, token), ErrorCode.PermissionDenied);
        var plan = await McpCallerAssertions.SdkSuccessAsync(await caller.PlanTenantPlacementAsync(
            new(seed.Partition.TenantId, seed.Partition.DatabaseId, [seed.Partition], state.Control), token));
        var repair = new PersistPlacementIntentRequest(Guid.NewGuid(), 0, plan.SignedPlan, state.Control);
        var healthyId = Guid.NewGuid();
        var healthy = await McpCallerAssertions.SdkSuccessAsync(
            await caller.PersistPlacementIntentAsync(healthyId, repair, token));
        await TenantCapacityPlacementRf3Assertions.RequirePendingAsync(healthy, plan.Plan, repair.IntentId);
        await SqlRf3Protocol.EqualAsync(healthy, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new { commandId = healthyId, request = repair }, token))).Value);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }

    private static async Task ConfigureAsync(KeyLoadClient source, KeyLoadClient target, string tenant,
        bool administrator, long epoch, ApiKeyRecord key, CancellationToken token)
    {
        var principal = new PrincipalRecord(PrincipalId, tenant, [], [])
        { ClusterAdministrator = administrator, PolicyEpoch = epoch };
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
