using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.RecoveryTests.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessRefusal
{
    private const int FirstVoter = 0;
    private const string InvalidMac = "not-a-native-mac";
    private const char InvalidLowercaseMacCharacter = 'A';

    internal static async Task RunAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        await RequireRefusalAsync(owners, original, InvalidMac, PartitionMovementProtocol.InvalidProof,
            cancellationToken);
        var invalidInner = new string(InvalidLowercaseMacCharacter, ReplicaTransportProtocol.HashHexCharacters);
        await RequireRefusalAsync(owners, original, invalidInner, PartitionMovementProtocol.Unavailable,
            cancellationToken);
    }

    private static async Task RequireRefusalAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, string innerSignature, string expectedDetail,
        CancellationToken cancellationToken)
    {
        var before = await owners.CaptureAsync(cancellationToken);
        var sender = owners.Source.NativeSender;
        var address = await sender.ResolveAsync(owners.Control, FirstVoter, cancellationToken);
        var signed = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            NativeSerialization.Serialize(original), innerSignature, owners.Control.Owner.VoterIds[FirstVoter], address);
        using var observation = owners.Source.NativeObservation.Arm(signed.Nonce, owners.Control.Owner.VoterIds[FirstVoter]);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = await Assert.ThrowsAsync<KeyLoadException>(() => sender.ExchangeAsync(address, signed, cancellationToken))
                ?? throw new InvalidOperationException(ControlledPartitionMovementNativeProcessProtocol.Invalid);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
            if (expectedDetail == PartitionMovementProtocol.Unavailable)
            { await Assert.That(failure.Data[PartitionMovementGrainServiceClient.InvalidReplyStage]).IsNull(); }
            await Assert.That(failure.Message).IsEqualTo(expectedDetail);
            var after = await owners.CaptureAsync(cancellationToken);
            await ControlledPartitionMovementNativeProcessColdAssertions.ExactAsync(before, after);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(observation.RequireAndReport, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
