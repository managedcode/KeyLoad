using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Requires unchanged live authority and complete no-effects around the exact original native packet.</summary>
internal static class ControlledPartitionMovementNativeNonceRefusal
{
    internal static async Task RejectAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementGrainServiceClient sender, SiloAddress address, PartitionMovementGrainServiceRequest packet,
        DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        var source = owners.Source;
        var target = owners.Target;
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var sourcePosition = source.Store.Position;
        var targetPosition = target.Store.Position;
        var sourceIndex = source.Log.State.LastIndex;
        var targetIndex = target.Log.State.LastIndex;
        var actualSourceAddress = source.CallerSiloAddress;
        await Assert.That(packet.CallerSiloAddress).IsEqualTo(actualSourceAddress);
        await Assert.That(packet.DestinationSiloAddress).IsEqualTo(address.ToParsableString());
        await Assert.That(packet.DestinationSiloAddress).IsEqualTo(actualSourceAddress);
        await Assert.That(packet.ExpiresAt > source.Clock.GetUtcNow()).IsTrue();
        await Assert.That(originalExpiry > source.Clock.GetUtcNow()).IsTrue();
        var refusal = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => sender.ExchangeAsync(address, packet, cancellationToken));
        await Assert.That(refusal).IsNotNull();
        var actual = refusal ?? throw new InvalidOperationException("The duplicate original packet did not refuse.");
        await Assert.That(actual.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(actual.Message).IsEqualTo(PartitionMovementProtocol.Unavailable);
        await Assert.That(packet.ExpiresAt > source.Clock.GetUtcNow()).IsTrue();
        await Assert.That(originalExpiry > source.Clock.GetUtcNow()).IsTrue();
        await Assert.That(source.CallerSiloAddress).IsEqualTo(actualSourceAddress);
        await Assert.That(source.Store.Position).IsEqualTo(sourcePosition);
        await Assert.That(target.Store.Position).IsEqualTo(targetPosition);
        await Assert.That(source.Log.State.LastIndex).IsEqualTo(sourceIndex);
        await Assert.That(target.Log.State.LastIndex).IsEqualTo(targetIndex);
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(source.Store).SequenceEqual(sourceImage)).IsTrue();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(target.Store).SequenceEqual(targetImage)).IsTrue();
    }
}
