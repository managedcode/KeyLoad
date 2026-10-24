using KeyLoad.Core.ResourceExecution;
using D = KeyLoad.UnitTests.DatabaseComposition.CompositionPrefixPrepareData;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionPrefixPrepareSeed
{
    private const string IdColumn = "id";

    internal static async Task ConfigureAsync(CompositionPrefixPrepareOwner owner)
    {
        foreach (var (name, kind) in new[] { (D.From, ResourceKind.Collection), (D.To, ResourceKind.Collection),
            (D.Graph, ResourceKind.Graph), (D.RoundGraph, ResourceKind.Graph), (D.Queue, ResourceKind.WorkQueue) })
        {
            var definition = new ResourceDefinition(name, kind, owner.Source.Partition.TransactionDomainId)
            {
                RelationalSchema = kind == ResourceKind.Collection
                    ? new(IdColumn, [new(IdColumn, RelationalColumnType.Text)]) : null
            };
            var request = new ConfigureResourceRequest(owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, definition);
            var original = owner.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(), D.Root,
                owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
            var lifetime = ResourceLifetimeIdentity.Create(original, request.TenantId, request.DatabaseId, definition);
            var bytes = OrleansSerialization.Serialize(original);
            await CompositionPrefixPrepareAssertions.EqualAsync(definition, owner.Submit(original).Get<ResourceDefinition>());
            await Assert.That(OrleansSerialization.Serialize(owner.Retained(original)).SequenceEqual(bytes)).IsTrue();
            owner.Lifetimes.Add(name, lifetime);
            await CompositionPrefixPrepareAssertions.EqualAsync(lifetime, owner.Store.Read(view =>
                ResourceLifetimeCatalog.Read(view, request.TenantId, request.DatabaseId, name, definition)));
        }
    }

    internal static async Task<string> QueueAdministratorAsync(CompositionPrefixPrepareOwner owner)
    {
        const string administratorId = "prefix-queue-administrator";
        var partition = owner.Source.Partition;
        var administrator = new PrincipalRecord(administratorId, partition.TenantId,
            [new(partition.DatabaseId, D.Queue, Capability.QueuePublish | Capability.QueueCancel | Capability.QueueInspect),
             new(partition.DatabaseId, D.Graph, Capability.GraphWrite),
             new(partition.DatabaseId, D.From, Capability.DocumentsRead),
             new(partition.DatabaseId, D.To, Capability.DocumentsRead)], [])
        {
            ClusterAdministrator = true
        };
        var original = owner.Database.CreateCommand(OperationKind.ConfigurePrincipal, Guid.NewGuid(), D.Root,
            owner.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(new ConfigurePrincipalRequest(administrator)));
        await CompositionPrefixPrepareAssertions.EqualAsync(administrator, owner.Submit(original).Get<PrincipalRecord>());
        return administratorId;
    }

    internal static EntityRef Bound(CompositionPrefixPrepareOwner owner, string collection, string id)
        => new(owner.Source.Partition, collection, id)
        {
            Lifetime = new(owner.Lifetimes[collection].Incarnation.Value)
        };

    internal static EntityRef Fresh(CompositionPrefixPrepareOwner owner, string collection, string id)
        => new(owner.Source.Partition, collection, id);

    internal static async Task VerticesAsync(CompositionPrefixPrepareOwner owner, bool first = true)
    {
        if (first)
        {
            await CompositionPrefixPrepareCommands.CommitAsync(owner,
                [new PutDocument(D.From, D.First, D.Json, D.Absent)],
                [new(D.Put, D.From, D.First, D.FirstRevision)]);
        }
        await CompositionPrefixPrepareCommands.CommitAsync(owner,
            [new PutDocument(D.To, D.Second, D.OtherJson, D.Absent)],
            [new(D.Put, D.To, D.Second, D.FirstRevision)]);
    }
}
