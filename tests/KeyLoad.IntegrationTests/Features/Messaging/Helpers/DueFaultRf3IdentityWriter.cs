using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3IdentityWriter
{
    private const Capability ScheduleCapabilities = Capability.SchedulerManage | Capability.QueuePublish
        | Capability.QueueInspect;
    private const Capability DestinationCapabilities = ScheduleCapabilities;

    internal static async Task<DueFaultRf3Creator> CreateAsync(Aspire.Hosting.DistributedApplication app,
        PartitionRef partition, QueueLaneRef recurringLane, QueueLaneRef sagaLane, QueueLaneRef timeoutLane,
        NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, RequestCqrsRf3Protocol.Node1);
        var administrator = new KeyLoadClient(http, profile.AdminKey, IntegrationClientOptions.Execution());
        await ConfigureQueueAsync(administrator, partition, recurringLane.Queue, cancellationToken)
            .ConfigureAwait(false);
        await ConfigureQueueAsync(administrator, partition, sagaLane.Queue, cancellationToken)
            .ConfigureAwait(false);
        await ConfigureQueueAsync(administrator, partition, timeoutLane.Queue, cancellationToken)
            .ConfigureAwait(false);
        var principal = CreatePrincipal(partition, recurringLane, sagaLane, timeoutLane);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), principal,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var secret = CreateSecret();
        var credential = CreateCredential(principal.Id, secret);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false)).IsTrue();
        return new(secret);
    }

    private static async Task ConfigureQueueAsync(KeyLoadClient administrator, PartitionRef partition, string queue,
        CancellationToken cancellationToken)
    {
        var definition = new ResourceDefinition(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            FieldPolicies = [Policy(DueFaultRf3Protocol.PayloadField)],
            HeaderPolicies = [Policy(DueFaultRf3Protocol.HeaderField)]
        };
        var configured = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(configured.Name).IsEqualTo(queue);
    }

    private static SensitiveFieldPolicy Policy(string path)
        => new(path, DueFaultRf3Protocol.Classification, DueFaultRf3Protocol.ReadGrant,
            DueFaultRf3Protocol.UseGrant, DueFaultRf3Protocol.WriteGrant);

    private static PrincipalRecord CreatePrincipal(PartitionRef partition, QueueLaneRef recurringLane,
        QueueLaneRef sagaLane, QueueLaneRef timeoutLane)
    {
        var grants = ImmutableArray.Create(
            Scope(partition, recurringLane, ScheduleCapabilities),
            Scope(partition, sagaLane, ScheduleCapabilities),
            Scope(partition, timeoutLane, DestinationCapabilities));
        return new PrincipalRecord(DueFaultRf3Protocol.PrincipalPrefix + Guid.NewGuid().ToString("N"),
            partition.TenantId, grants,
            [DueFaultRf3Protocol.ReadGrant, DueFaultRf3Protocol.UseGrant, DueFaultRf3Protocol.WriteGrant])
        { ClusterAdministrator = false };
    }

    private static ScopeGrant Scope(PartitionRef partition, QueueLaneRef lane, Capability capabilities)
        => new(partition.DatabaseId, lane.Queue, capabilities | Capability.QueueConsume | Capability.QueueAck);

    private static string CreateSecret()
        => DueFaultRf3Protocol.ApiKeyPrefix + Guid.NewGuid().ToString("N") + DueFaultRf3Protocol.SecretSeparator
            + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(DueFaultRf3Protocol.SecretBytes));

    private static ApiKeyRecord CreateCredential(string principalId, string secret)
    {
        var keyId = secret[..secret.IndexOf(DueFaultRf3Protocol.SecretSeparator, StringComparison.Ordinal)];
        return new(keyId, principalId,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret))));
    }
}
