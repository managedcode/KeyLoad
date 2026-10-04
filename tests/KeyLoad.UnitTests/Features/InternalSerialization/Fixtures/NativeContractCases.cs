using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal static class NativeContractCases
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

    internal static object Family(NativeContractFamily family) => family switch
    {
        NativeContractFamily.Document => new DocumentRecord(Entity, 1, Json, new(), At),
        NativeContractFamily.Authorization => new PrincipalRecord(Id, Tenant, [new(Database, Resource, Capability.DocumentsRead)], []),
        NativeContractFamily.Messaging => new DeliveryClaims(new(Partition, Resource), Id, Id, 1, 1, Guid.NewGuid()),
        NativeContractFamily.Events => new EventRecord(new(Partition, Resource, Id), 1, 1, new(Id, Field, Json), At),
        NativeContractFamily.Graph => new EdgeRecord(Id, Entity, Entity, Label, Json, 1),
        NativeContractFamily.TimeSeries => new SampleRecord(Id, new(Id, At, 1), 1, Json),
        NativeContractFamily.Vector => new VectorRecord(Id, Field, new(Id, 2, DistanceMetric.Cosine, Model, Version), [1, 2], 1),
        NativeContractFamily.Blob => new BlobMetadata(new(Partition, Resource, Id), 1, Guid.NewGuid(), 2, 1, Id, new(), At),
        NativeContractFamily.Outbox => new OutboxEntry(1, 0, new(Guid.NewGuid(), Partition.AtomicPartitionId, 1, 1), At, Mutations[0], new(Field, Resource, Id, 1)),
        NativeContractFamily.Relational => new RelationalSchema(Id, [new(Id, RelationalColumnType.Text)]),
        NativeContractFamily.Query => new AstQueryRequest(Partition, new(Resource, null, [new(Field, Field)], new Comparison(new FieldOperand(Field), Id, new ParameterOperand(Field)), [], 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(family))
    };
}

internal enum NativeContractFamily { Document, Authorization, Messaging, Events, Graph, TimeSeries, Vector, Blob, Outbox, Relational, Query }
