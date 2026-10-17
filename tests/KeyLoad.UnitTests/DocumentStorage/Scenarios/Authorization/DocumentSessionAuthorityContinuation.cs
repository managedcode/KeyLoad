using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentSessionAuthorityContinuation
{
    private const string WrongOwner = "The receiving document owner is not the admitted physical shard.";
    private const string PutKind = "putDocument";
    private static readonly Guid FreshId = Guid.Parse("1a7075d5-26b4-4f88-b227-b8036f60f844");

    internal static async Task RunAsync(RemoteDocumentNativeFixture fixture)
    {
        var image = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        var position = fixture.Destination.Store.Position;
        OwnedDocumentReadResultV1? value = null;
        var foreign = Assert.ThrowsExactly<KeyLoadException>(() => value = DocumentSessionAuthorityColdAssertions.Read(
            fixture, fixture.Receipt.Token, owner: PhysicalOwnerDirectoryWholeFlow.Control.Owner));
        await Assert.That(foreign.Code).IsEqualTo(ErrorCode.OwnershipLost);
        await Assert.That(foreign.Message).IsEqualTo(WrongOwner);
        await Assert.That(value).IsNull();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var cancelled = Assert.ThrowsExactly<OperationCanceledException>(() => value =
            DocumentSessionAuthorityColdAssertions.Read(fixture, fixture.Receipt.Token, token: cancellation.Token));
        await Assert.That(cancelled.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(value).IsNull();
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
        var request = new CommandRequest(FreshId, RemoteDocumentNativeFixture.Partition,
            [new PutDocument(RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId,
                RemoteDocumentNativeFixture.OriginalJson, RemoteDocumentNativeFixture.First, ExplicitReplacement: true)]);
        var operation = RemoteDocumentNativeFixture.Operation(fixture.Destination, OperationKind.Batch, FreshId, request);
        var result = fixture.Destination.Database.Apply(operation, RemoteDocumentNativeFixture.Second);
        var receipt = new CommitReceipt(FreshId, fixture.Receipt.Token with
        {
            Position = RemoteDocumentNativeFixture.Second
        },
            [new(PutKind, RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId,
                RemoteDocumentNativeFixture.Second)], DurabilityProfile.ProcessDurable);
        await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(
            NativeSerialization.Serialize(new OperationResult(null) { NativeValue = receipt }))).IsTrue();
        await DocumentSessionAuthorityColdAssertions.ReplayAsync(fixture, operation, receipt);
        image = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        position = fixture.Destination.Store.Position;
        fixture.Destination.Reopen();
        await DocumentSessionAuthorityColdAssertions.LiteralAsync(fixture, fixture.Receipt.Token,
            RemoteDocumentNativeFixture.Second, position);
        await DocumentSessionAuthorityColdAssertions.LiteralAsync(fixture, receipt.Token,
            RemoteDocumentNativeFixture.Second, position);
        await DocumentSessionAuthorityColdAssertions.ReplayAsync(fixture, fixture.Write, fixture.Receipt);
        await DocumentSessionAuthorityColdAssertions.ReplayAsync(fixture, operation, receipt);
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
    }
}
