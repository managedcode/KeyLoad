using KeyLoad.UnitTests.Features.DocumentStorage;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class RemotePartitionQueryNativeAssertions
{
    private const string AccessPath = "bounded-full-scan";

    internal static async Task PageAsync(PartitionQueryPageV1 page, RemoteDocumentNativeFixture fixture,
        long sourceEpoch = RemotePartitionQueryNativeFlow.SourceEpoch,
        long sourceSchema = RemoteDocumentNativeFixture.First)
    {
        var expected = new PartitionQueryPageV1(RemoteDocumentNativeFixture.Version,
        [
            new(RemoteDocumentNativeFixture.Reference, new(RemoteDocumentNativeFixture.DocumentId,
                RemoteDocumentNativeFixture.First, RemoteDocumentNativeFixture.ProjectedJson, true, [RemoteDocumentNativeFixture.Secret])),
            new(new(RemotePartitionQueryNativeFlow.Local, RemoteDocumentNativeFixture.Collection,
                RemoteDocumentNativeFixture.DocumentId), new(RemoteDocumentNativeFixture.DocumentId,
                RemoteDocumentNativeFixture.First, RemotePartitionQueryNativeFlow.SourceProjected, true, [RemoteDocumentNativeFixture.Secret]))
        ],
        [
            new(RemoteDocumentNativeFixture.Partition, fixture.Destination.Store.Position,
                RemotePartitionQueryNativeFlow.DestinationEpoch, RemoteDocumentNativeFixture.First, AccessPath),
            new(RemotePartitionQueryNativeFlow.Local, fixture.Source.Store.Position,
                sourceEpoch, sourceSchema, AccessPath)
        ], true);
        await Assert.That(JsonDefaults.Serialize(page).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task StateAsync(RemoteDocumentNativeFixture fixture, string[] source, string[] destination,
        long sourcePosition, long destinationPosition)
    {
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(source)).IsTrue();
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(destination)).IsTrue();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(destinationPosition);
    }
}
