using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchHybridSeed
{
    internal const string VectorField = "/embedding";
    internal const int TwoDimensions = 2;
    internal const long CurrentEpoch = 4;
    internal const double TextWeight = 2;
    internal const double VectorWeight = 1;
    private const string SpaceId = "distributed-execution-space";
    private const string ModelId = "literal-execution-model";
    private const string ModelVersion = "one";
    internal static readonly VectorSpace Space = new(SpaceId, TwoDimensions, DistanceMetric.DotProduct, ModelId, ModelVersion);

    internal static void Seed(QueryFixture fixture)
    {
        var flow = new QueryDataset(fixture);
        flow.Seed();
        Configure(fixture.Source);
        Configure(fixture.Destination);
        WriteVector(fixture.Source, QueryDataset.Local, [1f, 0f]);
        WriteVector(fixture.Destination, QueryFixture.Partition, [0f, 1f]);
    }

    internal static SearchRequest Request(PartitionRef partition)
        => new(partition, QueryFixture.Collection,
            TextField: QueryFixture.Title, Text: "source destination",
            VectorField: VectorField, Vector: [1f, 0f], Space: Space,
            Limit: TwoDimensions, TextWeight: TextWeight, VectorWeight: VectorWeight);

    private static void Configure(DocumentPatchAtomicityOwner owner)
    {
        var principal = new PrincipalRecord(QueryFixture.Reader, QueryFixture.Tenant,
            [new(QueryFixture.Partition.DatabaseId, QueryFixture.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], [])
        {
            PolicyEpoch = CurrentEpoch
        };
        _ = owner.Database.Apply(QueryFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();
    }

    private static void WriteVector(DocumentPatchAtomicityOwner owner, PartitionRef partition, ImmutableArray<float> values)
    {
        var command = Guid.NewGuid();
        var request = new CommandRequest(command, partition,
            [new PutVector(QueryFixture.Collection, QueryFixture.DocumentId,
                VectorField, values, Space, QueryFixture.First)]);
        _ = owner.Database.Apply(QueryFixture.Operation(owner, OperationKind.Batch, command, request))
            .Get<CommitReceipt>();
    }
}
