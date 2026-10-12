using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains a genuinely successful original native packet before proving duplicate nonce refusal.</summary>
internal static class ControlledPartitionMovementNativeNonce
{
    private const int FirstVoter = 0;

    internal static async Task ProveAsync(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, PartitionMovePhaseResult expected, CancellationToken cancellationToken)
    {
        var source = owners.Source;
        var request = original with
        {
            CallerSiloAddress = source.CallerSiloAddress,
            Envelope = original.Envelope with { Nonce = Guid.NewGuid() }
        };
        using var context = new PartitionMovementTransportContext(source.Runtime.Node, source.Silo,
            source.Partition, source.Runtime.Membership, source.Clock);
        var address = await context.ResolveAsync(owners.Control, FirstVoter, readOnly: false, cancellationToken);
        var body = NativeSerialization.Serialize(request);
        var signature = context.Sign(body, PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect);
        var sender = source.NativeSender;
        var packet = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            body, signature, owners.Control.Owner.VoterIds[FirstVoter], address);
        var response = await sender.ExchangeAsync(address, packet, cancellationToken);
        var verifier = new PartitionMovementTransportReplyVerifier(context);
        var authenticated = verifier.ReadReplyMacPurpose(response, local: true,
            PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect);
        await verifier.VerifyReplyAsync(FirstVoter, owners.Control, request, authenticated.Value, address,
            PartitionMoveOriginalDispatchIdentity.Digest(request.CommandId, request.Envelope), cancellationToken);
        await Assert.That(authenticated.Value.Reply.Error).IsNull();
        await Assert.That(authenticated.Value.Reply.SafeDetail).IsNull();
        var phase = GrainNativePayload.Read<GrainValue>(authenticated.Value.Reply.Payload).Value as PartitionMovePhaseResult;
        await Assert.That(JsonDefaults.Serialize(phase).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        await ControlledPartitionMovementNativeNonceRefusal.RejectAsync(owners, sender, address, packet,
            request.Envelope.ExpiresAt, cancellationToken);
    }
}
