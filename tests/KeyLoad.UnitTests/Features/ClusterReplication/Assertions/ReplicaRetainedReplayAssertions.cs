using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaRetainedReplayCut(long Position, string[] Rows, long LastIndex,
    long CommittedIndex, long AppliedIndex, long Term, byte[] Entry);

internal static class ReplicaRetainedReplayAssertions
{
    internal static Dictionary<string, ReplicaRetainedReplayCut> Capture(RequestCqrsCohortRuntimeFixture fixture)
        => ReplicaBarrierDeadlineFlow.Voters.ToDictionary(voter => voter, voter => Capture(fixture.Node(voter)), StringComparer.Ordinal);

    internal static ReplicaRetainedReplayCut Capture(RequestCqrsCohortNativeNode node)
        => new(node.Journal.Database.Store.Position,
            QueueWholeFlowStorage.Bytes((KeyLoad.Storage.ZoneTree.ZoneTreeStore)node.Journal.Database.Store),
            node.Journal.Log.State.LastIndex, node.Journal.Log.State.CommittedIndex,
            node.Journal.Database.LastApplied, node.Journal.Log.State.Term,
            NativeSerialization.Serialize(node.Journal.Log.ReadEntry(node.Journal.Log.State.LastIndex)));

    internal static async Task UnchangedAsync(RequestCqrsCohortRuntimeFixture fixture,
        Dictionary<string, ReplicaRetainedReplayCut> expected, bool sameTerm)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            await UnchangedAsync(fixture.Node(voter), expected[voter], sameTerm);
        }
    }

    internal static async Task UnchangedAsync(RequestCqrsCohortNativeNode node,
        ReplicaRetainedReplayCut expected, bool sameTerm)
    {
        var current = Capture(node);
        await Assert.That(current.Position).IsEqualTo(expected.Position);
        await Assert.That(current.Rows.SequenceEqual(expected.Rows, StringComparer.Ordinal)).IsTrue();
        await Assert.That(current.LastIndex).IsEqualTo(expected.LastIndex);
        await Assert.That(current.CommittedIndex).IsEqualTo(expected.CommittedIndex);
        await Assert.That(current.AppliedIndex).IsEqualTo(expected.AppliedIndex);
        await Assert.That(current.Entry.AsSpan().SequenceEqual(expected.Entry)).IsTrue();
        if (sameTerm)
        {
            await Assert.That(current.Term).IsEqualTo(expected.Term);
        }
    }

    internal static async Task ModelsAsync(RequestCqrsCohortRuntimeFixture fixture,
        ReplicaRetainedReplaySeed seed, CancellationToken token)
    {
        foreach (var voter in ReplicaBarrierDeadlineFlow.Voters)
        {
            var node = fixture.Node(voter);
            var cut = Capture(node);
            var document = node.Journal.Database.GetDocument(P.Root,
                new(P.Partition, P.Collection, P.DocumentId), cancellationToken: token);
            var expected = new DocumentResult(new(P.Partition, P.Collection, P.DocumentId),
                ReplicaRetainedReplayProducer.Single, P.Json, false, []);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(expected),
                NativeSerialization.Serialize(document));
            var blobs = new BlobStorageOperations(node.Journal.Database);
            var read = blobs.Read(P.Root, new(seed.Blob, ReplicaRetainedReplayProducer.Single,
                ReplicaRetainedReplayProducer.Empty, ReplicaRetainedReplayProducer.Bytes.Length), token);
            await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(seed.Metadata),
                NativeSerialization.Serialize(read.Metadata));
            await Assert.That(read.Bytes.Span.SequenceEqual(ReplicaRetainedReplayProducer.Bytes)).IsTrue();
            await UnchangedAsync(node, cut, sameTerm: true);
        }
    }
}
