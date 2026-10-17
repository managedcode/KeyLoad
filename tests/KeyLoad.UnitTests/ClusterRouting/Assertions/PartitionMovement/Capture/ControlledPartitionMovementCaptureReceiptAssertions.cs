using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Compares the complete source settlement to literal original command, authority and absolute log index.</summary>
internal static class ControlledPartitionMovementCaptureReceiptAssertions
{
    private const int Version = 1;
    private const long OriginalSourceCut = 13;
    private const long CaptureNativeIndex = 17;

    internal static async Task AssertAsync(ControlledPartitionMovementLoopbackCorpus corpus,
        PartitionMovePhaseResult acceptedFence, KeyLoad.Clustering.PartitionMovementCaptureHandle handle,
        PartitionMovePhaseResult actual, long additionalSeedEntries = 0)
    {
        var fence = acceptedFence.Fence
            ?? throw new InvalidOperationException("The actual originally verified source fence is absent.");
        await Assert.That(handle.Descriptor.Version).IsEqualTo(Version);
        await Assert.That(handle.Descriptor.MoveId).IsEqualTo(ControlledPartitionMovementPrepareRequest.MoveId);
        await Assert.That(handle.Descriptor.SourceCut).IsEqualTo(checked(OriginalSourceCut + additionalSeedEntries));
        await Assert.That(JsonDefaults.Serialize(handle.Descriptor.SourcePlacement)
            .SequenceEqual(JsonDefaults.Serialize(fence.SourcePlacement))).IsTrue();
        var expected = new PartitionMovePhaseResult(ControlledPartitionMovementPrepareRequest.MoveId,
            PartitionMovePeerStage.Capture, new(ControlledPartitionMovementCaptureRequest.CaptureCommandId,
                corpus.Control.Owner, checked(CaptureNativeIndex + additionalSeedEntries), acceptedFence.Journal.ControlIntentDigest),
            null, fence, null, null);
        await Assert.That(JsonDefaults.Serialize(actual).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }
}
