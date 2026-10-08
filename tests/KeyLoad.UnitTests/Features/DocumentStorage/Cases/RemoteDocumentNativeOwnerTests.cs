using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class RemoteDocumentNativeOwnerTests
{
    private const string ForeignOwner = "The atomic partition is owned by another physical shard.";
    private const string ChangedFence = "The remote document read authority changed.";
    private const string LocalPartitionKey = "local";
    private const string LocalJson = "{\"title\":\"source\"}";

    [Test]
    public Task AcOwnerDoc002RegisteredRemoteMapRejectsLocalWriteAndReplaysFailureBeforeHealthyLocalEffect()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            await RemoteDocumentNativeAssertions.ReceiptAsync(fixture);
            var destinationBefore = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var destinationPosition = fixture.Destination.Store.Position;
            var id = Guid.NewGuid();
            var command = new CommandRequest(id, RemoteDocumentNativeFixture.Partition,
                [new PutDocument(RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId, LocalJson)]);
            var operation = RemoteDocumentNativeFixture.Operation(fixture.Source, OperationKind.Batch, id, command);
            var denied = fixture.Source.Database.Apply(operation);
            await Assert.That(denied.Error).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(denied.SafeDetail).IsEqualTo(ForeignOwner);
            var sourceFailed = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var failedPosition = fixture.Source.Store.Position;
            var replay = fixture.Source.Database.Apply(operation);
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(denied))).IsTrue();
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(sourceFailed)).IsTrue();
            await Assert.That(fixture.Source.Store.Position).IsEqualTo(failedPosition);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(destinationBefore)).IsTrue();
            await Assert.That(fixture.Destination.Store.Position).IsEqualTo(destinationPosition);
            var local = RemoteDocumentNativeFixture.Partition with { PartitionKey = LocalPartitionKey };
            var healthyId = Guid.NewGuid();
            var healthy = fixture.Source.Database.Apply(RemoteDocumentNativeFixture.Operation(fixture.Source,
                OperationKind.Batch, healthyId, new CommandRequest(healthyId, local,
                    [new PutDocument(RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId, LocalJson)])),
                RemoteDocumentNativeFixture.First);
            await Assert.That(healthy.Error).IsNull();
            var document = fixture.Source.Database.GetDocument(RemoteDocumentNativeFixture.Root,
                new(local, RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId));
            var expected = new DocumentResult(new(local, RemoteDocumentNativeFixture.Collection, RemoteDocumentNativeFixture.DocumentId),
                RemoteDocumentNativeFixture.First, LocalJson, false, []);
            await Assert.That(JsonDefaults.Serialize(document).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        });

    [Test]
    public Task AcOwnerDoc003And005SourcePolicyChangeInvalidatesCapturedRouteWithoutDestinationEffect()
        => RemoteDocumentNativeAssertions.RunAsync(async fixture =>
        {
            fixture.Seed();
            var fence = fixture.Source.Database.CaptureRemoteDocumentRead(RemoteDocumentNativeFixture.Reader,
                RemoteDocumentNativeFixture.Reference, default)
                ?? throw new InvalidOperationException("The actual remote route was not captured.");
            await Assert.That(fence.Destination.Owner.PhysicalShardId)
                .IsEqualTo(PhysicalOwnerDirectoryWholeFlow.Destination.Owner.PhysicalShardId);
            fixture.Source.AddPrincipal(new(RemoteDocumentNativeFixture.Reader, RemoteDocumentNativeFixture.Tenant,
                [new(RemoteDocumentNativeFixture.Partition.DatabaseId, RemoteDocumentNativeFixture.Collection,
                    Capability.DocumentsRead)], [RemoteDocumentNativeFixture.Title])
            { PolicyEpoch = RemoteDocumentNativeFixture.Second });
            var sourceBefore = RemoteDocumentNativeAssertions.Image(fixture.Source);
            var sourcePosition = fixture.Source.Store.Position;
            var destinationBefore = RemoteDocumentNativeAssertions.Image(fixture.Destination);
            var rejected = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Source.Database.ValidateRemoteDocumentRead(fence,
                RemoteDocumentNativeFixture.Reference, default));
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.OwnershipLost);
            await Assert.That(rejected.Message).IsEqualTo(ChangedFence);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Source).SequenceEqual(sourceBefore)).IsTrue();
            await Assert.That(fixture.Source.Store.Position).IsEqualTo(sourcePosition);
            await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(destinationBefore)).IsTrue();
            var fresh = fixture.Source.Database.CaptureRemoteDocumentRead(RemoteDocumentNativeFixture.Reader,
                RemoteDocumentNativeFixture.Reference, default)
                ?? throw new InvalidOperationException("The fresh native route was not captured.");
            fixture.Source.Database.ValidateRemoteDocumentRead(fresh, RemoteDocumentNativeFixture.Reference, default);
            fixture.GrantDestination();
            var healthy = fixture.Destination.Database.ReadOwnedDocument(RemoteDocumentNativeFixture.Reader,
                RemoteDocumentNativeFixture.Tenant, new(RemoteDocumentNativeFixture.Reference, fixture.Receipt.Token),
                PhysicalOwnerDirectoryWholeFlow.Destination.Owner, default);
            await RemoteDocumentNativeAssertions.DocumentAsync(healthy, fixture.Destination, fixture.Destination.Store.Position);
        });
}
