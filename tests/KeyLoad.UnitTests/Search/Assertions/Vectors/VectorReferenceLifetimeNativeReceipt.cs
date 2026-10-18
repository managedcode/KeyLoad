using KeyLoad.UnitTests.Features.ResourceExecution;
using static KeyLoad.UnitTests.Features.Search.VectorReferenceLifetimeNativeProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class VectorReferenceLifetimeNativeReceipt
{
    private const string ProjectionMutation = "applyVectorProjection";

    internal static async Task AssertAsync(NativeDropRuntimeProfile profile, CommandRequest original,
        CommitReceipt actual, CancellationToken token)
    {
        var entry = await ControlledPartitionMovementNativeEntryReader.ReadAsync(profile.Source, original.CommandId, token);
        await Assert.That(entry.Operation).IsNotNull();
        await Assert.That(entry.Operation!.Value!.Input.Span.SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new(profile.Source.Store.Identity.Incarnation, NativeDropRuntimeProtocol.Partition.AtomicPartitionId,
                entry.Index), [new(ProjectionMutation, Target, NativeDropRuntimeProtocol.Row, FirstRevision)], DurabilityProfile.ProcessDurable, 1);
        await NativeDropRuntimeAssertions.SameAsync(expected, actual);
    }
}
