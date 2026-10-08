
namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Publishes one genuine new original-scope batch after abort, then checks literal receipt, replay and cold visibility.</summary>
internal static class ControlledPartitionMovementAbortHealthy
{
    private const long HealthyReplicaIndex = 146;
    private const long OwnershipEpoch = 1;
    private const long Revision = 1;
    private const string DocumentId = "post-abort-1";
    private const string Json = "{\"state\":\"healthy\"}";
    private const string Kind = "putDocument";
    private static readonly Guid CommandId = Guid.Parse("458d7392-a366-49fb-8e03-2b052b9579a9");

    internal static async Task ExecuteAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementLoopbackCorpus corpus, CancellationToken cancellationToken)
    {
        var command = new CommandRequest(CommandId, ControlledPartitionMovementCorpus.Partition,
            [new PutDocument(ControlledPartitionMovementCorpus.Collection, DocumentId, Json)]);
        var original = source.Database.CreateNativeOperation(OperationKind.Batch, CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, source.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(command));
        var result = source.Journal.Submit(original, cancellationToken);
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        var expected = new CommitReceipt(CommandId, new(corpus.Control.Owner.Incarnation,
            ControlledPartitionMovementCorpus.Partition.AtomicPartitionId, HealthyReplicaIndex, OwnershipEpoch),
            [new(Kind, ControlledPartitionMovementCorpus.Collection, DocumentId, Revision)],
            DurabilityProfile.ProcessDurable);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(receipt).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var bytes = NativeSerialization.Serialize(receipt);
        var replay = source.Journal.Submit(original, cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay.Get<CommitReceipt>()).SequenceEqual(bytes)).IsTrue();
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(HealthyReplicaIndex);
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        source.Reopen();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
        var reference = new EntityRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, DocumentId);
        var document = source.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId, reference,
            cancellationToken: cancellationToken);
        await Assert.That(JsonDefaults.Serialize(document).SequenceEqual(JsonDefaults.Serialize(
            new DocumentResult(reference, Revision, Json, false, [])))).IsTrue();
    }
}
