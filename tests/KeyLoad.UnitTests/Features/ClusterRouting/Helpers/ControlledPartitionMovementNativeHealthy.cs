using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Proves a fresh acknowledged operation, exact original replay and literal cold retained document.</summary>
internal static class ControlledPartitionMovementNativeHealthy
{
    private static readonly Guid CommandId = Guid.Parse("2e9455b1-38d7-4c80-a56f-d5596e36b7ca");
    private const string DocumentId = "knowledge-followup";
    private const string Json = "{\"text\":\"healthy\"}";
    private const long InitialRevision = 1;

    private static CommandRequest Request() => new(CommandId, ControlledPartitionMovementCorpus.Partition,
        [new PutDocument(ControlledPartitionMovementCorpus.Collection, DocumentId, Json)]);

    internal static async Task<CommitReceipt> SubmitAsync(ControlledPartitionMovementNativeHost source,
        CancellationToken cancellationToken)
    {
        var reply = await source.SubmitAsync(OperationKind.Batch, CommandId, PhysicalShardCatalogFixture.RootPrincipalId,
            NativeSerialization.Serialize(Request()), cancellationToken);
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(source, CommandId, cancellationToken);
        var expected = new CommitReceipt(CommandId, new(source.Store.Identity.Incarnation,
            ControlledPartitionMovementCorpus.Partition.AtomicPartitionId, entry.Index, InitialRevision),
            [new("putDocument", ControlledPartitionMovementCorpus.Collection, DocumentId, InitialRevision)],
            DurabilityProfile.QuorumProcessDurable);
        await VerifyReplyAsync(reply, expected);
        await ReadAsync(source, cancellationToken);
        return expected;
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeHost source, CommitReceipt expected,
        CancellationToken cancellationToken)
    {
        var reply = await source.SubmitAsync(OperationKind.Batch, CommandId, PhysicalShardCatalogFixture.RootPrincipalId,
            NativeSerialization.Serialize(Request()), cancellationToken);
        await VerifyReplyAsync(reply, expected);
        await ReadAsync(source, cancellationToken);
    }

    private static async Task VerifyReplyAsync(GrainOperationReply reply, CommitReceipt expected)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var actual = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt;
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    private static async Task ReadAsync(ControlledPartitionMovementNativeHost source, CancellationToken cancellationToken)
    {
        var reference = new EntityRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, DocumentId);
        var actual = source.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, cancellationToken: cancellationToken);
        var expected = new DocumentResult(reference, InitialRevision, Json, false, []);
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
