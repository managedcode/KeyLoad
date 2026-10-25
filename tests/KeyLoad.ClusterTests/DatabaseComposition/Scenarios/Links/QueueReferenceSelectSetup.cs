using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class QueueReferenceSelectSetup
{
    private const string IdFieldKey = "id";
    internal static async Task<QueueReferenceSelectState> RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers admin,
        CancellationToken token)
    {
        var partition = new PartitionRef("queue-select-" + Guid.NewGuid().ToString("N"), "database", "domain", Guid.NewGuid().ToString("N"));
        foreach (var queue in new[] { QueueReferenceSelectProtocol.Queue, QueueReferenceSelectProtocol.InvalidQueue, QueueReferenceSelectProtocol.EmptyQueue })
        {
            await ConfigureAsync(admin, partition, new(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
            {
                FieldPolicies = [new("/to", "reference", RawReadGrant: QueueReferenceSelectProtocol.RefRead, RawUseGrant: QueueReferenceSelectProtocol.RefUse)]
            }, token);
        }
        var targetLifetime = await ConfigureAsync(admin, partition, new(QueueReferenceSelectProtocol.Target, ResourceKind.Collection, partition.TransactionDomainId)
        {
            RelationalSchema = new(IdFieldKey, [new(IdFieldKey, RelationalColumnType.Text), new("name", RelationalColumnType.Text)]),
            FieldPolicies = [new("/name", "private", RawReadGrant: QueueReferenceSelectProtocol.NameRead)]
        }, token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            QueueReferenceSelectProtocol.Queue, Capability.Query | Capability.QueueInspect, token);
        var command = new CommandRequest(Guid.NewGuid(), partition,
        [
            new PutDocument(QueueReferenceSelectProtocol.Target, "c1", QueueReferenceSelectProtocol.FirstJson, Access: new(OwnerId: identity.Principal.Id)),
            new PutDocument(QueueReferenceSelectProtocol.Target, "c2", QueueReferenceSelectProtocol.HiddenJson, Access: new(OwnerId: "other-owner")),
            new EnqueueMessage(QueueReferenceSelectProtocol.Queue, "a", QueueReferenceSelectProtocol.Payload(partition, "c1", 7)),
            new EnqueueMessage(QueueReferenceSelectProtocol.Queue, "b", QueueReferenceSelectProtocol.Payload(partition, "m", 7)),
            new EnqueueMessage(QueueReferenceSelectProtocol.Queue, "c", QueueReferenceSelectProtocol.Payload(partition, "c2", 7)),
            new EnqueueMessage(QueueReferenceSelectProtocol.Queue, "d", QueueReferenceSelectProtocol.Payload(partition, "c1", 2)),
            new EnqueueMessage(QueueReferenceSelectProtocol.Queue, "e", "{\"score\":7}"),
            new EnqueueMessage(QueueReferenceSelectProtocol.InvalidQueue, "wrong",
                QueueReferenceSelectProtocol.Payload(partition with { PartitionKey = "foreign" }, "c1", 7))
        ]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        var state = new QueueReferenceSelectState(partition, identity, command, receipt, targetLifetime);
        await PolicyAsync(admin, state, 2, allow: false, names: false, token);
        await QueueReferenceSelectSources.VerifyAsync(admin, state, token);
        return state;
    }

    internal static async Task PolicyAsync(RequestCqrsClusterCallers admin, QueueReferenceSelectState state,
        long epoch, bool allow, bool names, CancellationToken token)
    {
        var fields = new List<string> { QueueReferenceSelectProtocol.RefRead };
        if (allow)
        {
            fields.Add(QueueReferenceSelectProtocol.RefUse);
        }
        if (names)
        {
            fields.Add(QueueReferenceSelectProtocol.NameRead);
        }
        var principal = state.Identity.Principal with
        {
            PolicyEpoch = epoch,
            RestrictRows = true,
            OwnerId = state.Identity.Principal.Id,
            Grants = [new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.Queue, Capability.Query | Capability.QueueInspect),
                new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.EmptyQueue, Capability.Query | Capability.QueueInspect),
                new(state.Partition.DatabaseId, QueueReferenceSelectProtocol.Target, Capability.Query | Capability.DocumentsRead)],
            FieldGrants = [.. fields]
        };
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token));
    }

    private static async Task<EntityRefLifetime> ConfigureAsync(RequestCqrsClusterCallers admin, PartitionRef partition,
        ResourceDefinition definition, CancellationToken token)
    {
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(creationId, creation, token));
        return ResourceCreationLifetime.Create(creationId, creation);
    }
}
