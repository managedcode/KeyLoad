using System.Text.Json;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.DatabaseComposition;

internal static class CompositionReplayFixture
{
    internal const string Collection = "entities";
    internal const string Graph = "relations";
    internal const string Queue = "work";
    internal const string Principal = "reader";
    internal const string First = "first";
    internal const string Second = "second";
    internal const string Third = "third";
    internal const string Message = "source";
    internal const string Prefix = "derived-";
    internal const string Label = "related";
    internal const string OtherPartition = "other-partition";

    internal static TestDatabase NewDatabase()
    {
        var db = new TestDatabase();
        db.Configure(Collection, ResourceKind.Collection);
        db.Configure(Graph, ResourceKind.Graph);
        db.Configure(Queue, ResourceKind.WorkQueue);
        db.Commit(new PutDocument(Collection, First, "{}", Access: new(Principal)),
            new PutDocument(Collection, Second, "{}", Access: new(Principal)),
            new PutDocument(Collection, Third, "{}", Access: new(Principal)));
        return db;
    }

    internal static EntityRef Vertex(TestDatabase db, string id) => new(db.Partition, Collection, id);

    internal static bool SameEffects(CommitReceipt left, CommitReceipt right)
        => left.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision))
            .SequenceEqual(right.Mutations.Select(effect => (effect.Kind, effect.Resource, effect.Id, effect.Revision)));

    internal static string Link(TestDatabase db, string from, string to)
        => JsonSerializer.Serialize(new QueueGraphLink(Vertex(db, from), Vertex(db, to), Label), JsonDefaults.Options);

    internal static Command<DatabaseCommand> Forward(TestDatabase db, string principal = "root")
    {
        var id = Guid.NewGuid();
        return db.Database.CreateCommand(OperationKind.Batch, id, principal, TimeProvider.System.GetUtcNow(),
            OrleansSerialization.Serialize(new CommandRequest(id, db.Partition, [new QueueToGraph(Graph, Queue, Prefix)])));
    }

    internal static void ConfigureReader(TestDatabase db, Capability? revoked = null)
    {
        var grants = new[]
        {
            new ScopeGrant(db.Partition.DatabaseId, Collection, Capability.DocumentsRead),
            new ScopeGrant(db.Partition.DatabaseId, Queue, Capability.QueueInspect),
            new ScopeGrant(db.Partition.DatabaseId, Graph, Capability.GraphWrite)
        };
        var previous = db.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(Principal)));
        var active = revoked is null ? grants : grants.Where(grant => grant.Capabilities != revoked).ToArray();
        var record = new PrincipalRecord(Principal, db.Partition.TenantId, [.. active], [])
        {
            RestrictRows = true,
            OwnerId = Principal,
            PolicyEpoch = (previous?.PolicyEpoch ?? 0) + 1
        };
        db.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record)).Get<PrincipalRecord>();
    }
}
