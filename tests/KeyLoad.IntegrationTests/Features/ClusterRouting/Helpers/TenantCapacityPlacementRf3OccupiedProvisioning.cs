using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedProvisioning
{
    private const long PackedDirectoryRevision = 2;
    private const int CurrentVersion = 1;
    private const long OriginalPrincipalEpoch = 1;
    internal static async Task<TenantDedicatedOwnerRf3State> CreateAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State capacity,
        CancellationToken token)
    {
        var packed = await TenantDedicatedOwnerRf3Provisioning.CreateAsync(wave, seed, token);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, TwoRf3MembershipProtocol.Node4,
            wave.Profile.AdminKey, async root =>
            {
                var identity = packed.Sibling;
                var definition = new PrincipalRecord(identity.PrincipalId, identity.Partition.TenantId,
                    [new(identity.Partition.DatabaseId, RequestCqrsRf3Protocol.AdminCollection,
                        Capability.DocumentsRead | Capability.DocumentsWrite)], [])
                { PolicyEpoch = OriginalPrincipalEpoch, ClusterAdministrator = false };
                _ = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(identity.Partition.TenantId, identity.Partition.DatabaseId,
                        new(RequestCqrsRf3Protocol.AdminCollection, ResourceKind.Collection,
                            identity.Partition.TransactionDomainId)), token));
                await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
                    await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), definition, token)));
                var key = identity.Secret[..identity.Secret.IndexOf('.', StringComparison.Ordinal)];
                var credential = new ApiKeyRecord(key, identity.PrincipalId,
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity.Secret))));
                await Assert.That((await McpCallerAssertions.SdkStatusAsync(
                    await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), credential, token))).IsSuccess).IsTrue();
                return definition;
            }, token);
        var bindId = Guid.NewGuid();
        var bind = new BindAtomicPartitionPlacementRequest(CurrentVersion, PackedDirectoryRevision, capacity.Control,
            seed.Directory.ControlOwner.PhysicalShardId);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.BindAtomicPartitionPlacementAsync(bindId, bind, token))).IsTrue();
        await Assert.That((await McpCallerAssertions.SuccessAsync<bool>(await seed.Official.CallAsync(
            McpCallerTools.AdminPartitionPlacementBind, new { commandId = bindId, request = bind }, token))).Value).IsTrue();
        var configure = new ConfigurePlacementPolicyRequest(capacity.OriginalPolicy.Policy.Revision,
            capacity.HealthyPolicy, capacity.Control);
        var configureId = Guid.NewGuid();
        await SqlRf3Protocol.EqualAsync(capacity.HealthyPolicy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(configureId, configure, token)));
        await SqlRf3Protocol.EqualAsync(capacity.HealthyPolicy,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.ConfigureTool, new { commandId = configureId, request = configure }, token))).Value);
        return packed;
    }
}
