using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Rejects an invalid inner proof through the genuine outer signer and addressed native receiver.</summary>
internal static class ControlledPartitionMovementNativeProofFlow
{
    private const string InvalidInnerProof = "not-a-native-mac";
    private const int FirstVoter = 0;

    internal static async Task RejectAsync(ControlledPartitionMovementNativeHost source,
        ControlledPartitionMovementNativeHost target, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var sourceIndex = source.Log.State.LastIndex;
        var targetIndex = target.Log.State.LastIndex;
        var sender = source.NativeSender;
        var address = await sender.ResolveAsync(receiver, FirstVoter, cancellationToken);
        var request = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            NativeSerialization.Serialize(original), InvalidInnerProof,
            receiver.Owner.VoterIds[FirstVoter], address);
        KeyLoadException? refusal = null;
        try
        { await sender.ExchangeAsync(address, request, cancellationToken); }
        catch (KeyLoadException error)
        { refusal = error; }
        await Assert.That(refusal).IsNotNull();
        await Assert.That(refusal?.Code).IsEqualTo((ErrorCode?)ErrorCode.Unauthenticated);
        await Assert.That(refusal?.Message).IsEqualTo(PartitionMovementProtocol.Unavailable);
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(target.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
    }
}
