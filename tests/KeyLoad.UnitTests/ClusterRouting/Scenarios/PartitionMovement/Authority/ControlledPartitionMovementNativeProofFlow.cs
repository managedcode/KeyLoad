using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Rejects an invalid inner proof through the genuine outer signer and addressed native receiver.</summary>
internal static class ControlledPartitionMovementNativeProofFlow
{
    private const string InvalidInnerProof = "not-a-native-mac";
    private const string OuterSignatureLengthStage = "outer-signature-length";
    private const int FirstVoter = 0;
    private static readonly string InvalidMac = new('0', KeyLoad.Clustering.ReplicaTransportProtocol.HashHexCharacters);

    internal static async Task RejectAsync(ControlledPartitionMovementNativeHost source,
        ControlledPartitionMovementNativeHost target, RegisteredPhysicalOwnerV1 receiver,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        var before = new OriginalImages(ControlledPartitionMovementRawImage.Bytes(source.Store),
            ControlledPartitionMovementRawImage.Bytes(target.Store), source.Store.Position,
            target.Store.Position, source.Log.State.LastIndex, target.Log.State.LastIndex);
        await RejectOneAsync(source, receiver, original, malformed: true, cancellationToken);
        await UnchangedAsync(source, target, before);
        await RejectOneAsync(source, receiver, original, malformed: false, cancellationToken);
        await UnchangedAsync(source, target, before);
    }

    private static async Task RejectOneAsync(ControlledPartitionMovementNativeHost source,
        RegisteredPhysicalOwnerV1 receiver, PartitionMovementTransportRequest original,
        bool malformed, CancellationToken cancellationToken)
    {
        var sender = source.NativeSender;
        var address = await sender.ResolveAsync(receiver, FirstVoter, cancellationToken);
        var request = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            NativeSerialization.Serialize(original), malformed ? InvalidInnerProof : InvalidMac,
            receiver.Owner.VoterIds[FirstVoter], address);
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var observation = source.NativeObservation.Arm(request.Nonce, receiver.Owner.VoterIds[FirstVoter]);
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                KeyLoadException? refusal = null;
                try
                {
                    await sender.ExchangeAsync(address, request, cancellationToken);
                }
                catch (KeyLoadException error)
                {
                    refusal = error;
                }
                await Assert.That(refusal).IsNotNull();
                await Assert.That(refusal?.Code).IsEqualTo((ErrorCode?)ErrorCode.Unauthenticated);
                await Assert.That(refusal?.Message).IsEqualTo(
                    malformed ? PartitionMovementProtocol.InvalidProof : PartitionMovementProtocol.Unavailable);
                if (malformed)
                {
                    await Assert.That(refusal?.Data[PartitionMovementGrainServiceClient.InvalidReplyStage])
                        .IsEqualTo(OuterSignatureLengthStage);
                }
                else
                {
                    await Assert.That(refusal?.Data.Contains(PartitionMovementGrainServiceClient.InvalidReplyStage))
                        .IsFalse();
                }
            }, failures);
            KeyLoad.Server.ServerFailureObserver.Observe(observation.RequireAndReport, failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task UnchangedAsync(ControlledPartitionMovementNativeHost source,
        ControlledPartitionMovementNativeHost target, OriginalImages original)
    {
        await Assert.That(source.Store.Position).IsEqualTo(original.SourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(original.TargetPosition);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(original.SourceIndex);
        await Assert.That(target.Log.State.LastIndex).IsEqualTo(original.TargetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(original.Source)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(original.Target)).IsTrue();
    }

    private sealed record OriginalImages(string[] Source, string[] Target, long SourcePosition,
        long TargetPosition, long SourceIndex, long TargetIndex);
}
