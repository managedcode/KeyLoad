using System.Collections.Immutable;
using KeyLoad.UnitTests.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class DistributedSearchHybridNativeSeed
{
    internal const string VectorField = "/embedding";
    internal const int TwoDimensions = 2;
    internal const long CurrentEpoch = 4;
    internal const double TextWeight = 2;
    internal const double VectorWeight = 1;
    private const string SpaceId = "distributed-native-space";
    private const string ModelId = "literal-native-model";
    private const string ModelVersion = "one";
    internal static readonly VectorSpace Space = new(SpaceId, TwoDimensions, DistanceMetric.DotProduct, ModelId, ModelVersion);

    internal static void Seed(RemoteDocumentNativeFixture fixture)
    {
        var flow = new RemotePartitionQueryNativeFlow(fixture);
        flow.Seed();
        Configure(fixture.Source);
        Configure(fixture.Destination);
        WriteVector(fixture.Source, RemotePartitionQueryNativeFlow.Local, [1f, 0f]);
        WriteVector(fixture.Destination, RemoteDocumentNativeFixture.Partition, [0f, 1f]);
    }

    internal static SearchRequest Request(PartitionRef partition)
        => new(partition, RemoteDocumentNativeFixture.Collection,
            TextField: RemoteDocumentNativeFixture.Title, Text: "source destination",
            VectorField: VectorField, Vector: [1f, 0f], Space: Space,
            Limit: TwoDimensions, TextWeight: TextWeight, VectorWeight: VectorWeight);

    private static void Configure(PhysicalShardCatalogFixture owner)
    {
        var principal = new PrincipalRecord(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
            [new(RemoteDocumentNativeFixture.Partition.DatabaseId, RemoteDocumentNativeFixture.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)], [])
        { PolicyEpoch = CurrentEpoch };
        _ = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.ConfigurePrincipal,
            Guid.NewGuid(), new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();
    }

    private static void WriteVector(PhysicalShardCatalogFixture owner, PartitionRef partition, ImmutableArray<float> values)
    {
        var command = Guid.NewGuid();
        var request = new CommandRequest(command, partition,
            [new PutVector(RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId,
                VectorField, values, Space, RemoteDocumentNativeFixture.First)]);
        _ = owner.Database.Apply(RemoteDocumentNativeFixture.Operation(owner, OperationKind.Batch, command, request))
            .Get<CommitReceipt>();
    }
}
