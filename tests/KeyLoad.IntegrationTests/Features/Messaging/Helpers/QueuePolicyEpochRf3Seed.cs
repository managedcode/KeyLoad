using System.Security.Cryptography;
using System.Text;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueuePolicyEpochRf3Seed(QueueLaneRef Lane, PrincipalRecord Principal,
    string Credential, MessageInspection Future)
{
    internal const string Message = "original";
    internal const string Healthy = "healthy";
    internal const string Payload = "{\"work\":1}";
    internal const string Headers = "{\"kind\":\"policy-fence\"}";
    internal const string FutureQueue = "future-policy-work";
    internal const Capability Granted = Capability.QueueConsume | Capability.QueueAck
        | Capability.QueueRenew | Capability.QueueInspect;

    internal static Task<QueuePolicyEpochRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        string administrator, CancellationToken token)
        => RequestCqrsRf3Callers.RunOwnedAsync<QueuePolicyEpochRf3Seed>(wave.Application, TwoRf3MembershipProtocol.Node1,
            administrator, async callers =>
        {
            var partition = new PartitionRef("protected-parent-tenant", "queue-policy-database",
                "queue-policy-domain", Guid.NewGuid().ToString("N"));
            var lane = new QueueLaneRef(partition, "policy-work");
            foreach (var queue in new[] { lane.Queue, FutureQueue })
            {
                _ = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                    new(partition.TenantId, partition.DatabaseId,
                        new(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)), token));
            }
            var principal = new PrincipalRecord("queue-policy-" + Guid.NewGuid().ToString("N"), partition.TenantId,
                [new(partition.DatabaseId, lane.Queue, Granted),
                 new(partition.DatabaseId, FutureQueue, Capability.QueueInspect)], []);
            principal = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigurePrincipalAsync(
                Guid.NewGuid(), principal, token));
            await Assert.That(principal.PolicyEpoch).IsEqualTo(1L);
            var keyId = "queue-policy-key-" + Guid.NewGuid().ToString("N");
            var secret = keyId + "." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            _ = await McpCallerAssertions.SdkStatusAsync(await callers.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
                new(keyId, principal.Id, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))), token));
            var publication = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, Message, Payload, Headers),
                 new EnqueueMessage(FutureQueue, Message, Payload, Headers,
                    NotBefore: TimeProvider.System.GetUtcNow().AddHours(1))]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(publication, token));
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, publication, token))).Value);
            var future = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(
                new(new(partition, FutureQueue), Message), token));
            await Assert.That(future).IsNotNull();
            await Assert.That(future!.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Assert.That(future.Metadata.Attempts).IsEqualTo(0);
            return new(lane, principal, secret, future);
        }, token);
}
