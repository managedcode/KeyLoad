using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record DistributedSearchRf3Seed(RemotePartitionQueryRf3Seed Original,
    CommandRequest SourceDocuments, CommitReceipt SourceDocumentsReceipt,
    CommandRequest SourceVector, CommitReceipt SourceVectorReceipt,
    CommandRequest DestinationVector, CommitReceipt DestinationVectorReceipt)
{
    internal const string LongId = "long";
    internal const string MissingId = "missing";
    internal const string LongJson = "{\"title\":\"source source source source source source source source source source\",\"secret\":\"long-private-canary\"}";
    internal const string LongProjected = "{\"title\":\"source source source source source source source source source source\"}";
    internal const string MissingJson = "{\"other\":\"visible\",\"secret\":\"missing-private-canary\"}";
    internal const long Revision = 1;
    internal const long InitialEpoch = 3;
    internal const int Version = 1;
    internal const int ResultLimit = 3;
    internal const int Dimension = 2;
    internal const double TextWeight = 2;
    internal const double VectorWeight = 1;
    internal const string VectorField = "/embedding";
    internal static readonly VectorSpace Space = new("distributed-rf3-space", Dimension,
        DistanceMetric.DotProduct, "independent-rf3-model", "one");

    internal static async Task<DistributedSearchRf3Seed> CreateAsync(TwoRf3MembershipWave wave,
        KeyLoadClient source, KeyLoadClient destination, CancellationToken token)
    {
        var original = await RemotePartitionQueryRf3Seed.CreateAsync(wave, source, destination, true, token).ConfigureAwait(false);
        await ConfigureAsync(source, original.Destination.Principal, InitialEpoch, token).ConfigureAwait(false);
        var documents = new CommandRequest(Guid.NewGuid(), RemotePartitionQueryRf3Seed.Local,
            [new PutDocument(RemoteDocumentRf3Protocol.Collection, LongId, LongJson, RemoteDocumentRf3Protocol.UnboundRevision),
             new PutDocument(RemoteDocumentRf3Protocol.Collection, MissingId, MissingJson, RemoteDocumentRf3Protocol.UnboundRevision)]);
        var documentReceipt = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(documents, token));
        var sourceVector = Vector(RemotePartitionQueryRf3Seed.Local, LongId, [1f, 0f]);
        var destinationVector = Vector(RemoteDocumentRf3Protocol.Partition, RemoteDocumentRf3Protocol.Document, [0f, 1f]);
        var sourceReceipt = await McpCallerAssertions.SdkSuccessAsync(await source.CommitAsync(sourceVector, token));
        var destinationReceipt = await McpCallerAssertions.SdkSuccessAsync(await destination.CommitAsync(destinationVector, token));
        return new(original, documents, documentReceipt, sourceVector, sourceReceipt, destinationVector, destinationReceipt);
    }

    internal static async Task ConfigureAsync(KeyLoadClient administrator, PrincipalRecord original,
        long epoch, CancellationToken token)
    {
        var expected = original with
        {
            PolicyEpoch = epoch,
            Grants = [new(RemoteDocumentRf3Protocol.Database, RemoteDocumentRf3Protocol.Collection,
                Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)]
        };
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await Assert.That(NativeSerialization.Serialize(actual).AsSpan().SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
    }

    internal static DistributedSearchRequestV1 Request() => new(Version,
        [RemoteDocumentRf3Protocol.Partition, RemotePartitionQueryRf3Seed.Local],
        new(RemoteDocumentRf3Protocol.Partition, RemoteDocumentRf3Protocol.Collection,
            TextField: RemotePartitionQueryRf3Seed.TitlePath, Text: "source destination",
            VectorField: VectorField, Vector: [1f, 0f], Space: Space, Limit: ResultLimit,
            TextWeight: TextWeight, VectorWeight: VectorWeight, Explain: true));

    private static CommandRequest Vector(PartitionRef partition, string id, ImmutableArray<float> values)
        => new(Guid.NewGuid(), partition, [new PutVector(RemoteDocumentRf3Protocol.Collection,
            id, VectorField, values, Space, Revision)]);
}
