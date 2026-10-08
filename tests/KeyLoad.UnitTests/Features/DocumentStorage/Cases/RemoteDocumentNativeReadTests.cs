using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteDocumentNativeReadTests
{
    private const string DocumentsDenied = "The principal cannot perform this operation in this scope.";

    [Test]
    public Task AcOwnerDoc003DestinationDenialThenFreshGrantReadReplayAndNativeReopenRetainFullLiteralState()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            await RemoteDocumentNativeAssertions.ReceiptAsync(fixture);
            var before = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var position = fixture.Destination.Store.Position;
            var denied = Assert.ThrowsExactly<KeyLoadException>(() => Read(fixture, default));
            await Assert.That(denied.Code).IsEqualTo(ErrorCode.PermissionDenied);
            await Assert.That(denied.Message).IsEqualTo(DocumentsDenied);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
            fixture.GrantDestination();
            before = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            position = fixture.Destination.Store.Position;
            await RemoteDocumentNativeAssertions.DocumentAsync(Read(fixture, default), fixture.Destination, position);
            var replay = fixture.Destination.Database.Apply(fixture.Write);
            await Assert.That(NativeSerialization.Serialize(replay.Get<CommitReceipt>())
                .SequenceEqual(NativeSerialization.Serialize(fixture.Receipt))).IsTrue();
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
            fixture.Destination.Reopen();
            await RemoteDocumentNativeAssertions.DocumentAsync(Read(fixture, default), fixture.Destination, position);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
        });

    [Test]
    public Task AcOwnerDoc005And006OriginalCancellationReturnsNoWitnessOrPartialDocumentBeforeLiteralHealthyRead()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            fixture.GrantDestination();
            var before = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var position = fixture.Destination.Store.Position;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync();
            OwnedDocumentReadResultV1? result = null;
            var rejected = Assert.ThrowsExactly<OperationCanceledException>(() => result = Read(fixture, cancellation.Token));
            await Assert.That(rejected.CancellationToken).IsEqualTo(cancellation.Token);
            await Assert.That(result).IsNull();
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
            await RemoteDocumentNativeAssertions.DocumentAsync(Read(fixture, default), fixture.Destination, position);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
        });

    private static OwnedDocumentReadResultV1 Read(RemoteDocumentNativeFixture fixture, CancellationToken token)
        => fixture.Destination.Database.ReadOwnedDocument(RemoteDocumentNativeFixture.Reader,
            RemoteDocumentNativeFixture.Tenant, new(RemoteDocumentNativeFixture.Reference, fixture.Receipt.Token),
            PhysicalOwnerDirectoryWholeFlow.Destination.Owner, token);
}
