using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3IdentityWriter
{
    internal static async Task<DueNoQuorumRf3Creator> CreateAsync(Aspire.Hosting.DistributedApplication app,
        PartitionRef partition, QueueLaneRef lane, NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var policy = new SensitiveFieldPolicy("/secret", DueNoQuorumRf3Protocol.Classification,
            DueNoQuorumRf3Protocol.FieldReadGrant, DueNoQuorumRf3Protocol.FieldUseGrant,
            DueNoQuorumRf3Protocol.FieldWriteGrant);
        var resource = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        { FieldPolicies = [policy], HeaderPolicies = [policy] };
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var administrator = new KeyLoadClient(http, profile.AdminKey);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(configured.Name).IsEqualTo(lane.Queue);
        var id = DueNoQuorumRf3Protocol.PrincipalPrefix + Guid.NewGuid().ToString("N");
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, lane.Queue,
                Capability.SchedulerManage | Capability.QueuePublish | Capability.QueueInspect)],
            [DueNoQuorumRf3Protocol.FieldReadGrant, DueNoQuorumRf3Protocol.FieldUseGrant,
                DueNoQuorumRf3Protocol.FieldWriteGrant]);
        var keyId = DueNoQuorumRf3Protocol.ApiKeyPrefix + Guid.NewGuid().ToString("N");
        var secret = keyId + DueNoQuorumRf3Protocol.SecretSeparator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(DueNoQuorumRf3Protocol.RandomSecretBytes));
        var key = new ApiKeyRecord(keyId, id,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
        _ = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), key, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)).IsTrue();
        return new(principal, secret);
    }

}
