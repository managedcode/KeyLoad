using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.GraphTraversal.GraphReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphReferenceLifetimeNativeReceipt
{
    private const string UpsertMutation = "upsertEdge";

    internal static async Task AssertAsync(NativeDropRuntimeProfile profile, CommandRequest original,
        CommitReceipt actual, string graph, CancellationToken token)
    {
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(profile.Source, original.CommandId, token);
        await Assert.That(entry.Operation).IsNotNull();
        await Assert.That(entry.Operation!.Value!.Input.Span.SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new(profile.Source.Store.Identity.Incarnation, NativeDropRuntimeProtocol.Partition.AtomicPartitionId,
                entry.Index), [new(UpsertMutation, graph, EdgeId, Revision)], DurabilityProfile.ProcessDurable, 1);
        await NativeDropRuntimeAssertions.SameAsync(expected, actual);
    }
}
