using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class ContractCases
{
    internal const string Json = "{\"amount\":1.00}";
    internal const string ExpectedFailure = "The malformed binary fixture did not fail.";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "domain";
    private const string PartitionKey = "key";
    private const string Resource = "records";
    private const string Id = "record";
    private const string Field = "value";
    private const string Model = "model";
    private const string Version = "version";
    private const string Label = "links";
    internal static PartitionRef Partition { get; } = new(Tenant, Database, Domain, PartitionKey);
    internal static EntityRef Entity { get; } = new(Partition, Resource, Id);
    internal static DateTimeOffset At { get; } = DateTimeOffset.UnixEpoch;

    internal static ImmutableArray<Mutation> Mutations =>
    [
        new PutDocument(Resource, Id, Json),
        new PatchDocument(Resource, Id, [new(Field, PatchKind.Set, Json)], 1),
        new DeleteDocument(Resource, Id, 1),
        new AppendEvents(Resource, Id, [new(Id, Field, Json)], ExpectedStreamRevision.Any),
        new PublishTopic(Resource, [new(Id, Field, Json)]),
        new EnqueueMessage(Resource, Id, Json),
        new UpsertEdge(Resource, Id, Entity, Entity, Label),
        new DeleteEdge(Resource, Id, 1),
        new AppendSamples(Resource, Id, [new(Id, At, 1)]),
        new PutVector(Resource, Id, Field, [1, 2], new(Id, 2, DistanceMetric.Cosine, Model, Version), 1)
    ];

    internal static object Family(ContractFamily family) => family switch
    {
        ContractFamily.Document => new DocumentRecord(Entity, 1, Json, new(), At),
        ContractFamily.Authorization => new PrincipalRecord(Id, Tenant, [new(Database, Resource, Capability.DocumentsRead)], []),
        ContractFamily.Messaging => new DeliveryClaims(new(Partition, Resource), Id, Id, 1, 1, Guid.NewGuid()),
        ContractFamily.Events => new EventRecord(new(Partition, Resource, Id), 1, 1, new(Id, Field, Json), At),
        ContractFamily.Graph => new EdgeRecord(Id, Entity, Entity, Label, Json, 1),
        ContractFamily.TimeSeries => new SampleRecord(Id, new(Id, At, 1), 1, Json),
        ContractFamily.Vector => new VectorRecord(Id, Field, new(Id, 2, DistanceMetric.Cosine, Model, Version), [1, 2], 1),
        ContractFamily.Outbox => new OutboxEntry(1, 0, new(Guid.NewGuid(), Partition.AtomicPartitionId, 1), At, Mutations[0], new(Field, Resource, Id, 1)),
        ContractFamily.Relational => new RelationalSchema(Id, [new(Id, RelationalColumnType.Text)]),
        ContractFamily.Query => new AstQueryRequest(Partition, new(Resource, null, [new(Field, Field)], new Comparison(new FieldOperand(Field), Id, new ParameterOperand(Field)), [], 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };
}
