using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.Core;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLifecyclePublicSeed
{
    internal static Task<QueueLifecyclePublicState> RunAsync(RequestCqrsClusterCallers root, int route, CancellationToken token)
        => RunAsync(root, route, false, token);

    internal static async Task<QueueLifecyclePublicState> RunAsync(RequestCqrsClusterCallers root, int route,
        bool byteSublimit, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var partition = new PartitionRef("queue-lifecycle-" + suffix, "database", "messaging", suffix);
        var queue = new ResourceDefinition(QueueLifecyclePublicProtocol.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueLifecyclePublicProtocol.One,
                MaxStoredMessages = QueueLifecyclePublicProtocol.Two,
                MaxDeadLetterMessages = byteSublimit ? null : QueueLifecyclePublicProtocol.One,
                MaxDeadLetterBytes = byteSublimit ? QueueLifecyclePublicProtocol.One : null
            },
            FieldPolicies = [new("/knowledge", "private-lifecycle")]
        };
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, queue), token));
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueLifecyclePublicProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)), token));
        var id = "operator-" + suffix;
        var key = id + ".keyload-execution-lifecycle-credential";
        var principal = new PrincipalRecord(id, partition.TenantId,
            [new(partition.DatabaseId, queue.Name, Capability.QueuePublish | Capability.QueueConsume | Capability.QueueAck
                | Capability.QueueInspect | Capability.DeadLettersRead | Capability.DeadLettersRedrive | Capability.QueueCancel | Capability.Query),
             new(partition.DatabaseId, QueueLifecyclePublicProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite | Capability.Query)], ["*"])
        {
            ClusterAdministrator = true
        };
        await ConfigureAsync(root, principal, key, token);
        var deniedId = "denied-" + suffix;
        var deniedKey = deniedId + ".keyload-execution-lifecycle-credential";
        await ConfigureAsync(root, principal with
        {
            Id = deniedId,
            FieldGrants = []
        }, deniedKey, token);
        return new(partition, key, deniedKey, route);
    }

    private static async Task ConfigureAsync(RequestCqrsClusterCallers root, PrincipalRecord principal, string key, CancellationToken token)
    {
        await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
        var saved = await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(),
            AuthorizationExecution.Credential(principal.Id, principal.Id, key), token));
        await Assert.That(saved.IsSuccess).IsTrue();
    }
}
