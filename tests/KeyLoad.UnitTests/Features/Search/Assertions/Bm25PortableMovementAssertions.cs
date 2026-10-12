using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.Search;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25PortableMovementAssertions
{
    internal static async Task HeadAsync(ControlledPartitionMovementNativeHost owner,
        Bm25CorpusCurrentV1 original, byte[] originalBytes, CancellationToken token)
    {
        var work = new ReadExecutionBudget(owner.Runtime.Core.DatabaseLimits, owner.Clock, token);
        var key = Bm25CorpusKeys.Current(original);
        var actual = owner.Store.Read(view => work.Read(view, key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(originalBytes)).IsTrue();
        Bm25CorpusShape.RequireRecord(key, actual, original.Partition, owner.Database.Limits);
    }

    internal static async Task UnchangedAsync(
        ImmutableArray<ControlledPartitionMovementNativeProcessSnapshot> before,
        ControlledPartitionMovementNativeHosts owners, CancellationToken token)
    {
        var after = await Bm25PortableMovementCapture.SixAsync(owners, token);
        await Assert.That(before.Length).IsEqualTo(6);
        await Assert.That(after.Length).IsEqualTo(6);
        for (var index = 0; index < before.Length; index++)
        {
            await Assert.That(NativeSerialization.Serialize(after[index]).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(before[index]))).IsTrue();
        }
    }

    internal static async Task<PartitionMoveResult> ResultAsync(GrainOperationReply reply, PartitionMoveRequest request)
    {
        await Assert.That(reply.Error).IsNull();
        await Assert.That(reply.SafeDetail).IsNull();
        var result = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as PartitionMoveResult
            ?? throw new InvalidOperationException("The authentic original movement result is absent.");
        await Assert.That(result.MoveId).IsEqualTo(request.MoveId);
        await Assert.That(result.Partition).IsEqualTo(request.Partition);
        await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Retired);
        await Assert.That(result.InstalledReceipt).IsNotNull();
        await Assert.That(result.PublishedPlacement).IsNotNull();
        await Assert.That(result.DestinationOwner.PhysicalShardId).IsEqualTo(request.DestinationPhysicalShardId);
        return result;
    }

    internal static async Task HealthyAsync(ControlledPartitionMovementNativeHost target, CancellationToken token)
    {
        var reference = new EntityRef(ControlledPartitionMovementCorpus.Partition,
            ControlledPartitionMovementCorpus.Collection, "bm25-moved-followup");
        var request = new CommandRequest(Guid.NewGuid(), reference.Partition,
            [new PutDocument(reference.Collection, reference.Id, """{"text":"healthy after BM25 move"}""")]);
        var reply = await target.SubmitAsync(OperationKind.Batch, request.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), token);
        await Assert.That(reply.Error).IsNull();
        var receipt = GrainNativePayload.Read<GrainValue>(reply.Payload).Value as CommitReceipt;
        await Assert.That(receipt!.CommandId).IsEqualTo(request.CommandId);
        var actual = target.Database.GetDocument(PhysicalShardCatalogFixture.RootPrincipalId,
            reference, cancellationToken: token);
        await Assert.That(actual!.Json).IsEqualTo("""{"text":"healthy after BM25 move"}""");
        var replay = await target.SubmitAsync(OperationKind.Batch, request.CommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, NativeSerialization.Serialize(request), token);
        await Assert.That(replay.Error).IsNull();
        await Assert.That(replay.Payload.Span.SequenceEqual(reply.Payload.Span)).IsTrue();
    }
}
