using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Requires exact repaired original authorization replay with no additional append or canonical effects.</summary>
internal static class ControlledPartitionMovementNoneControlReplay
{
    internal static async Task AssertAsync(ControlledPartitionMovementNode source,
        ServerRuntimeOptions runtime, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest originalRequest, OperationResult original,
        CancellationToken cancellationToken)
    {
        var before = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var replay = await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(source, runtime, admission,
            originalRequest with { Envelope = originalRequest.Envelope with { Nonce = Guid.NewGuid() } }, cancellationToken);
        await Assert.That(NativeSerialization.Serialize(replay)
            .SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store)
            .SequenceEqual(before, StringComparer.Ordinal)).IsTrue();
    }
}
