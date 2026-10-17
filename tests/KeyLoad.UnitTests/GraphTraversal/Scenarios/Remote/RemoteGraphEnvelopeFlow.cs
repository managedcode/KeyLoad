using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class RemoteGraphEnvelopeFlow
{
    internal const string Collection = "graph-envelope-documents";
    internal const string Graph = "graph-envelope-links";

    internal static GrainRequestCodec Create(TestDatabase database)
    {
        database.Configure(Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(Collection, "from", "{\"value\":1}"));
        database.Commit(new PutDocument(Collection, "to", "{\"value\":2}"));
        return new(database.Database, TimeProvider.System, UnitRoutingOptions.Routing());
    }

    internal static ReadOnlyMemory<byte> ReadPayload(TestDatabase database)
        => NativeSerialization.Serialize(new ReadRemoteIncomingGraphEdgesRequestV1(1,
            new(database.Partition, Collection, "to"), Graph, 1));

    internal static (Guid Id, ReadOnlyMemory<byte> Bytes) CommandPayload(TestDatabase database, OperationKind kind)
    {
        var id = Guid.NewGuid();
        var from = new EntityRef(database.Partition, Collection, "from");
        var to = new EntityRef(database.Partition, Collection, "to");
        return kind switch
        {
            OperationKind.UpsertRemoteGraphEdge => (id, NativeSerialization.Serialize(
                new UpsertRemoteGraphEdgeRequestV1(id, database.Partition, new(Graph, "edge", from, to, "links")))),
            OperationKind.DeleteRemoteGraphEdge => (id, NativeSerialization.Serialize(
                new DeleteRemoteGraphEdgeRequestV1(id, database.Partition, Graph, "edge"))),
            OperationKind.DeliverRemoteGraphEdge => (id, NativeSerialization.Serialize(
                new DeliverRemoteGraphEdgeRequestV1(id, database.Partition, Graph, "edge", to, 1))),
            _ => throw new InvalidOperationException("The native graph parent case is outside its fixed catalog.")
        };
    }

    internal static async Task RequireEnvelopeAsync(TestDatabase database, GrainRequestEnvelope actual,
        Guid requestId, string purpose, DateTimeOffset expiry, ReadOnlyMemory<byte> payload)
    {
        await Assert.That(actual.RequestId).IsEqualTo(requestId);
        await Assert.That(actual.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(actual.PrincipalId).IsEqualTo("root");
        await Assert.That(actual.Purpose).IsEqualTo(purpose);
        await Assert.That(actual.ExpiresAt).IsEqualTo(expiry);
        await Assert.That(actual.ExpiresAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(actual.Payload.Span.SequenceEqual(payload.Span)).IsTrue();
    }

    internal static async Task RequireRefusalAsync(TestDatabase database, GrainRequestCodec codec,
        GrainRequestEnvelope original, string before)
    {
        var wrong = original with
        {
            Purpose = original.ReadKind == GrainReadKind.RemoteGraphIncomingParent
            || original.CommandKind is not null ? RemoteGraphRequestScope.ReadPurpose : RemoteGraphRequestScope.ParentPurpose
        };
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => codec.VerifyRequest(database.Database.Sign(wrong), original.RequestId));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(GrainRoutingProtocol.AdministrationRequired);
        await Assert.That(RemoteGraphNativeProofFlow.Image(database)).IsEqualTo(before);
    }

    internal static async Task HealthyAsync(TestDatabase database, GrainRequestCodec codec)
    {
        var id = Guid.NewGuid();
        var command = new CommandRequest(id, database.Partition, [new PutDocument(Collection, "healthy", "{\"healthy\":true}")]);
        var bytes = NativeSerialization.Serialize(command);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = codec.VerifyRequest(codec.CreateCommand(firstId, "root", OperationKind.Batch, id, bytes), firstId);
        var second = codec.VerifyRequest(codec.CreateCommand(secondId, "root", OperationKind.Batch, id, bytes), secondId);
        var executor = new GrainCommandExecutor(database.Database, new EmbeddedCoordinator(database.Database),
            TimeProvider.System, UnitRoutingOptions.Routing());
        var initial = await SignedGrainRequestTestContext.ExecuteAsync(executor, first,
            database.Partition.AtomicPartitionId, TestContext.Current!.Execution.CancellationToken);
        var replay = await SignedGrainRequestTestContext.ExecuteAsync(executor, second,
            database.Partition.AtomicPartitionId, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(initial.Error).IsNull();
        await Assert.That(replay.Error).IsNull();
        await Assert.That(initial.Payload.Span.SequenceEqual(replay.Payload.Span)).IsTrue();
        var document = database.Database.GetDocument("root", new(database.Partition, Collection, "healthy"))!;
        await Assert.That(document.Revision).IsEqualTo(1L);
        await Assert.That(document.Json).IsEqualTo("{\"healthy\":true}");
    }
}
