using KeyLoad.Clustering;

namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsRoutingCases
{
    private const int PostStartFailedSequence = 2;
    private const string WriterPrefix = "request-route-writer-";
    private const string ForgedPrefix = "request-route-forged-";

    internal static async Task AcCrs001RequestRoutesAStableBatchThroughItsCommandGrain(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("first");
        var commandId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var firstRequest = Guid.NewGuid();
        var beforePosition = fixture.Database.Store.Position;
        var firstReply = await RequestCqrsCommandStream.InvokeAsync(fixture, writer, firstRequest, commandId,
            command, async () =>
            {
                await AssertDocumentMissingAsync(fixture, documentId);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforePosition);
            });

        await Assert.That(firstReply.Error).IsNull();
        var firstReceipt = ReadReceipt(firstReply);
        await Assert.That(firstReceipt.CommandId).IsEqualTo(commandId);
        await Assert.That(firstReceipt.Mutations.Length).IsEqualTo(1);
        var firstReceiptBytes = OrleansSerialization.Serialize(firstReceipt);
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
        var firstPayload = OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(firstReply));
        var committedPosition = fixture.Database.Store.Position;

        var retryRequest = Guid.NewGuid();
        await Assert.That(retryRequest).IsNotEqualTo(firstRequest);
        var retryCommand = Command(commandId, fixture.Database.Partition, documentId);
        var retryReply = await RequestCqrsCommandStream.InvokeAsync(fixture, writer, retryRequest, commandId,
            retryCommand, async () =>
            {
                await AssertDocumentRevisionAsync(fixture, documentId, 1);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(committedPosition);
            });

        await Assert.That(retryReply.Error).IsNull();
        await Assert.That(OrleansSerialization.Serialize(GrainReplyFactory.RequireValue(retryReply)).AsSpan().SequenceEqual(firstPayload)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(ReadReceipt(retryReply))
            .SequenceEqual(firstReceiptBytes)).IsTrue();
        await AssertDocumentRevisionAsync(fixture, documentId, 1);
        var durableReceipt = OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition, writer.Id, commandId)?.Get<CommitReceipt>()
            ?? throw new InvalidOperationException("The stable command outcome has no durable receipt.");
        await Assert.That(OrleansSerialization.Serialize(durableReceipt).SequenceEqual(firstReceiptBytes)).IsTrue();
    }

    internal static async Task AcCrs003RevokedCommandFailsSafelyAndFollowingRequestSucceeds(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("revoked");
        var healthyDocumentId = DocumentId("healthy");
        var commandId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var requestId = Guid.NewGuid();
        var envelope = fixture.Codec.CreateCommand(requestId, writer.Id, OperationKind.Batch, commandId,
            command);
        var revoked = writer with
        {
            Revoked = true,
            PolicyEpoch = writer.PolicyEpoch + 1
        };
        fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked));
        var deniedPosition = fixture.Database.Store.Position;

        var denied = await RequestCqrsCommandStream.InvokeAsync(fixture, envelope, writer, requestId, commandId,
            PostStartFailedSequence, expectedError: ErrorCode.Unauthenticated);
        await Assert.That(denied.Error).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(deniedPosition);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition, writer.Id, commandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, documentId);

        var root = GrainRequestAuthority.Reload(fixture.Database.Database, RequestCqrsRoutingTests.RootPrincipalId,
            TimeProvider.System);
        var healthyId = Guid.NewGuid();
        var healthyRequest = Guid.NewGuid();
        var healthyCommand = Command(healthyId, fixture.Database.Partition, healthyDocumentId);
        var healthy = await RequestCqrsCommandStream.InvokeAsync(fixture, root, healthyRequest, healthyId,
            healthyCommand);
        await Assert.That(healthy.Error).IsNull();
        await Assert.That(ReadReceipt(healthy).CommandId).IsEqualTo(healthyId);
        await AssertDocumentRevisionAsync(fixture, healthyDocumentId, 1);
    }

    internal static async Task AcCrs006MissingAndForgedContextFailBeforeCommandEffects(
        RequestCqrsHostedRuntimeFixture fixture)
    {
        fixture.Database.Configure(RequestCqrsRoutingTests.Collection, ResourceKind.Collection);
        var writer = PersistWriter(fixture, WriterPrefix);
        var documentId = DocumentId("missing");
        var forgedDocumentId = DocumentId("forged");
        var commandId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var command = Command(commandId, fixture.Database.Partition, documentId);
        var envelope = fixture.Codec.CreateCommand(requestId, writer.Id, OperationKind.Batch, commandId,
            command);
        var beforeMissing = fixture.Database.Store.Position;
        await RequestCqrsCommandStream.AssertPreStreamRefusalAsync(fixture, envelope, null, requestId,
            commandId, ErrorCode.TokenInvalidated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforeMissing);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition, writer.Id, commandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, documentId);

        var forged = PersistWriter(fixture, ForgedPrefix);
        var forgedRequest = Guid.NewGuid();
        var forgedCommandId = Guid.NewGuid();
        var forgedCommand = Command(forgedCommandId, fixture.Database.Partition,
            forgedDocumentId);
        var forgedToken = fixture.Codec.CreateCommand(forgedRequest, writer.Id, OperationKind.Batch, forgedCommandId,
            forgedCommand);
        var beforeForged = fixture.Database.Store.Position;
        await RequestCqrsCommandStream.AssertPreStreamRefusalAsync(fixture, forgedToken, forged,
            forgedRequest, forgedCommandId, ErrorCode.Unauthenticated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(beforeForged);
        await Assert.That(OutcomeStoreOracle.ReadPartition(fixture.Database.Store, fixture.Database.Partition, writer.Id, forgedCommandId)).IsNull();
        await AssertDocumentMissingAsync(fixture, forgedDocumentId);
    }

    private static string DocumentId(string purpose)
        => purpose + "-" + Guid.NewGuid().ToString("N");

    private static PrincipalRecord PersistWriter(RequestCqrsHostedRuntimeFixture fixture, string idPrefix)
    {
        var record = new PrincipalRecord(idPrefix + Guid.NewGuid().ToString("N"), fixture.Database.Partition.TenantId,
            [new ScopeGrant(fixture.Database.Partition.DatabaseId, RequestCqrsRoutingTests.Collection,
                Capability.DocumentsWrite)], []);
        return fixture.Database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(record))
            .Get<PrincipalRecord>();
    }

    private static CommandRequest Command(Guid commandId, PartitionRef partition, string documentId)
        => new(commandId, partition,
            [new PutDocument(RequestCqrsRoutingTests.Collection, documentId, RequestCqrsRoutingTests.DocumentJson)]);

    private static CommitReceipt ReadReceipt(GrainOperationReply reply)
        => GrainReplyFactory.RequireValue(reply).Value as CommitReceipt
            ?? throw new InvalidOperationException("The command reply did not contain its canonical CommitReceipt.");

    private static async Task AssertDocumentMissingAsync(RequestCqrsHostedRuntimeFixture fixture, string id)
    {
        var document = fixture.Database.Database.GetDocument(RequestCqrsRoutingTests.RootPrincipalId,
            new EntityRef(fixture.Database.Partition, RequestCqrsRoutingTests.Collection, id));
        await Assert.That(document).IsNull();
    }

    private static async Task AssertDocumentRevisionAsync(RequestCqrsHostedRuntimeFixture fixture, string id,
        long expectedRevision)
    {
        var document = fixture.Database.Database.GetDocument(RequestCqrsRoutingTests.RootPrincipalId,
            new EntityRef(fixture.Database.Partition, RequestCqrsRoutingTests.Collection, id));
        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Revision).IsEqualTo(expectedRevision);
    }
}
