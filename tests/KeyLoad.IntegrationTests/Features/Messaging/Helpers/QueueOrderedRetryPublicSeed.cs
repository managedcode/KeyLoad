using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueOrderedRetryPublicSeed
{
    internal static async Task<QueueOrderedRetryPublicState> RunAsync(RequestCqrsRf3Callers root, int route,
        QueueParkedHeadPolicy parkedHead, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef("ordered-retry-" + suffix, "database", "messaging", suffix);
        var queue = new ResourceDefinition(QueueLifecyclePublicProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueOrderedRetryPublicProtocol.Two,
                MaxStoredMessages = QueueOrderedRetryPublicProtocol.Two,
                MaxDeadLetterMessages = QueueOrderedRetryPublicProtocol.One,
                OrderingProfile = QueueOrderingProfile.StrictPerKey,
                ParkedHeadPolicy = parkedHead,
                RetryJitter = QueueRetryJitter.Full
            },
            FieldPolicies = [new("/knowledge", "private-lifecycle")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, queue), token));
        var id = "ordered-operator-" + suffix;
        var key = id + ".keyload-native-lifecycle-credential";
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, queue.Name, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck
                | Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive | Capability.QueueCancel | Capability.Query)], ["*"])
        { ClusterAdministrator = true };
        await ConfigureAsync(root, principal, key, token);
        var deniedId = "ordered-denied-" + suffix;
        var deniedKey = deniedId + ".keyload-native-lifecycle-credential";
        await ConfigureAsync(root, principal with { Id = deniedId, FieldGrants = [] }, deniedKey, token);
        return new(new(partition, key, deniedKey, route), parkedHead);
    }
    private static async Task ConfigureAsync(RequestCqrsRf3Callers root, PrincipalRecord principal, string key, CancellationToken token)
    {
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(principal.Id, principal.Id, key), token))).IsTrue();
    }
}
