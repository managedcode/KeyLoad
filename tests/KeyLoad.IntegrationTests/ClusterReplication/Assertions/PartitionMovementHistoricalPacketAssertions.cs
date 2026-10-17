using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Actual stopped pending packets retain historical identity across genuine silo generations.</summary>
internal static class PartitionMovementHistoricalPacketAssertions
{
    internal static async Task RestartedAsync(IReadOnlyList<ReplicaSiloDiscovery> before,
        IReadOnlyList<ReplicaSiloDiscovery> after, PartitionMoveParentPhase original)
    {
        foreach (var old in before)
        {
            var current = after.Single(value => value.VoterId == old.VoterId);
            await Assert.That(current.ClusterId).IsEqualTo(old.ClusterId);
            await Assert.That(current.Incarnation).IsEqualTo(old.Incarnation);
            await Assert.That(current.SiloAddress).IsNotEqualTo(old.SiloAddress);
            await Assert.That(current.TransportReady).IsTrue();
        }
        var packet = original.OriginalReceiverIssuePacket
            ?? throw new InvalidOperationException(MissingPacket);
        var body = NativeSerialization.Deserialize<PartitionMovementReceiverIssueRequest>(packet.OriginalRequestBytes.Span);
        await Assert.That(body.OriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(body.IssuanceNonce).IsEqualTo(packet.IssuanceNonce);
        await Assert.That(before.Any(value => value.VoterId == body.CallerVoter
            && value.SiloAddress == body.CallerSiloAddress)).IsTrue();
        await Assert.That(after.Any(value => value.SiloAddress == body.CallerSiloAddress)).IsFalse();
    }

    internal static async Task ExactAsync(PartitionMoveParentPhase original, PartitionMoveParentPhase retained)
    {
        var expected = original.OriginalReceiverIssuePacket
            ?? throw new InvalidOperationException(MissingPacket);
        var actual = retained.OriginalReceiverIssuePacket
            ?? throw new InvalidOperationException(MissingPacket);
        await Assert.That(Convert.ToHexString(actual.OriginalRequestBytes.Span))
            .IsEqualTo(Convert.ToHexString(expected.OriginalRequestBytes.Span));
        await Assert.That(actual.OriginalRequestSignature).IsEqualTo(expected.OriginalRequestSignature);
        await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(actual)))
            .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(expected)));
    }

    internal static async Task CancellationAsync(IReadOnlyList<ReplicaSiloDiscovery> before,
        IReadOnlyList<ReplicaSiloDiscovery> after, PartitionMoveParentPhase retained)
    {
        var attempt = retained.RetireCancellationAttempt ?? throw new InvalidOperationException(MissingPacket);
        var body = NativeSerialization.Deserialize<PartitionMovementRetireCancellationRequest>(attempt.OriginalRequestBytes.Span);
        await Assert.That(body.CancellationCommandId).IsEqualTo(attempt.CancellationCommandId);
        await Assert.That(before.Any(value => value.VoterId == body.CallerVoter
            && value.SiloAddress == body.CallerSiloAddress)).IsTrue();
        await Assert.That(after.Any(value => value.SiloAddress == body.CallerSiloAddress)).IsFalse();
    }

    private const string MissingPacket = "The actual retained signed historical movement packet is absent.";
}
