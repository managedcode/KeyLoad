using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityColdAssertions
{
    internal static OwnedDocumentReadResultV1 Read(RemoteDocumentNativeFixture fixture, CommitToken minimum,
        PhysicalShardRecord? owner = null, CancellationToken token = default)
        => fixture.Destination.Database.ReadOwnedDocument(RemoteDocumentNativeFixture.Reader,
            RemoteDocumentNativeFixture.Tenant, new(RemoteDocumentNativeFixture.Reference, minimum),
            owner ?? PhysicalOwnerDirectoryWholeFlow.Destination.Owner, token);

    internal static async Task UnchangedAsync(RemoteDocumentNativeFixture fixture,
        string[] sourceImage, long sourcePosition, string[] image, long position)
    {
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(sourceImage, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
    }

    internal static async Task ReplayAsync(RemoteDocumentNativeFixture fixture, ReplicatedOperation operation,
        CommitReceipt receipt)
    {
        var image = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var position = fixture.Destination.Store.Position;
        var expected = NativeSerialization.Serialize(new OperationResult(null) { NativeValue = receipt });
        await Assert.That(NativeSerialization.Serialize(fixture.Destination.Database.Apply(operation)).SequenceEqual(expected)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(fixture.Destination.Database.ResolveOutcome(operation)).SequenceEqual(expected)).IsTrue();
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
    }

    internal static async Task LiteralAsync(RemoteDocumentNativeFixture fixture, CommitToken token, long revision,
        long position)
    {
        var owner = PhysicalOwnerDirectoryWholeFlow.Destination.Owner;
        var actual = Read(fixture, token);
        var expected = new OwnedDocumentReadResultV1(new(RemoteDocumentNativeFixture.Reference, revision,
            RemoteDocumentNativeFixture.ProjectedJson, true, [RemoteDocumentNativeFixture.Secret]),
            fixture.Destination.Store.Identity.NodeId, owner.Incarnation, RemoteDocumentNativeFixture.Empty,
            new(RemoteDocumentNativeFixture.Version, RemoteDocumentNativeFixture.Partition, owner.PhysicalShardId,
                owner.Incarnation, owner.VoterIds, owner.PlacementEpoch, RemoteDocumentNativeFixture.Empty,
                RemoteDocumentNativeFixture.Empty, true), RemoteDocumentNativeFixture.Second, revision, position);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
