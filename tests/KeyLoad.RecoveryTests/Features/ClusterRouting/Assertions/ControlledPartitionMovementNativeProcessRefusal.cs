using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessRefusal
{
    private const int FirstVoter = 0;
    private const string InvalidMac = "not-a-native-mac";

    internal static async Task RunAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        var before = await owners.CaptureAsync(cancellationToken);
        var sender = owners.Source.NativeSender;
        var address = await sender.ResolveAsync(owners.Control, FirstVoter, cancellationToken);
        var signed = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            NativeSerialization.Serialize(original), InvalidMac, owners.Control.Owner.VoterIds[FirstVoter], address);
        var failure = await Assert.ThrowsAsync<KeyLoadException>(() => sender.ExchangeAsync(address, signed, cancellationToken))
            ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(failure.Message).IsEqualTo(PartitionMovementProtocol.Unavailable);
        var after = await owners.CaptureAsync(cancellationToken);
        await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(before, after);
    }
}
