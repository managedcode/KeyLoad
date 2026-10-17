using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Observes actual native peer rejection before any private issuer or log effect exists.</summary>
internal static class ControlledPartitionMovementAdmissionAssertions
{
    private const string InvalidMac = "not-a-native-mac";
    private const string InvalidProof = "The partition movement peer proof is invalid.";

    internal static async Task RejectAsync(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, PartitionMovementPeerAdmission admission,
        PartitionMovementTransportRequest original, CancellationToken cancellationToken)
    {
        var sourceImage = ControlledPartitionMovementRawImage.Bytes(source.Store);
        var targetImage = ControlledPartitionMovementRawImage.Bytes(target.Store);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        PartitionMovementTransportRequest? partial = null;
        var failure = (await Assert.ThrowsExactlyAsync<KeyLoadException>(async () =>
            partial = await admission.VerifyAsync(NativeSerialization.Serialize(original), InvalidMac,
                cancellationToken)))
            ?? throw new InvalidOperationException("The original native admission rejection is absent.");
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(failure.Message).IsEqualTo(InvalidProof);
        await Assert.That(partial).IsNull();
        await ControlledPartitionMovementPrepareFlow.UnchangedAsync(source, target, sourceImage, targetImage,
            position, index);
    }
}
