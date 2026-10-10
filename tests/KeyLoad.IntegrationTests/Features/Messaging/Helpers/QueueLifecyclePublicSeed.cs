using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicSeed
{
    internal static async Task<QueueLifecyclePublicState> RunAsync(RequestCqrsRf3Callers root, int route, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef("queue-lifecycle-" + suffix, "database", "messaging", suffix);
        var queue = new ResourceDefinition(QueueLifecyclePublicProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueLifecyclePublicProtocol.One,
                MaxStoredMessages = QueueLifecyclePublicProtocol.Two,
                MaxDeadLetterMessages = QueueLifecyclePublicProtocol.One
            },
            FieldPolicies = [new("/knowledge", "private-lifecycle")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, queue), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueLifecyclePublicProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var id = "operator-" + suffix;
        var key = id + ".keyload-native-lifecycle-credential";
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, queue.Name, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck
                | Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive | Capability.QueueCancel | Capability.Query),
             new(partition.DatabaseId, QueueLifecyclePublicProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)], ["*"])
        { ClusterAdministrator = true };
        await ConfigureAsync(root, principal, key, token);
        var deniedId = "denied-" + suffix;
        var deniedKey = deniedId + ".keyload-native-lifecycle-credential";
        await ConfigureAsync(root, principal with { Id = deniedId, FieldGrants = [] }, deniedKey, token);
        return new(partition, key, deniedKey, route);
    }

    private static async Task ConfigureAsync(RequestCqrsRf3Callers root, PrincipalRecord principal, string key, CancellationToken token)
    {
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        var saved = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            DatabaseEngine.Credential(principal.Id, principal.Id, key), token));
        await Assert.That(saved).IsTrue();
    }
}
