using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Measures only the original fully signed reply returned by the genuine addressed native grain service.</summary>
internal static class ControlledPartitionMovementNativeOuterExchange
{
    private const int FirstVoter = 0;

    internal static async Task<T> ExecuteAsync<T>(ControlledPartitionMovementNativeHosts owners,
        PartitionMovementTransportRequest original, CancellationToken token) where T : class
    {
        var source = owners.Source;
        var request = original with
        {
            CallerSiloAddress = source.CallerSiloAddress,
            Envelope = original.Envelope with
            {
                Nonce = Guid.NewGuid()
            }
        };
        using var context = new PartitionMovementTransportContext(source.Runtime.Node, source.Silo,
            source.Partition, source.Runtime.Membership, source.Clock);
        var address = await context.ResolveAsync(owners.Control, FirstVoter, readOnly: false, token);
        var body = NativeSerialization.Serialize(request);
        var purpose = PartitionMovementTransportReplyVerifier.ReceiverReplyPurpose.Effect;
        var sender = source.NativeSender;
        var packet = sender.CreateRequest(PartitionMovementGrainServiceChannel.Phase, query: false,
            body, context.Sign(body, purpose), owners.Control.Owner.VoterIds[FirstVoter], address);
        var response = await sender.ExchangeAsync(address, packet, token);
        var verifier = new PartitionMovementTransportReplyVerifier(context);
        var authenticated = verifier.ReadReplyMacPurpose(response, local: true, purpose);
        await verifier.VerifyReplyAsync(FirstVoter, owners.Control, request, authenticated.Value, address,
            PartitionMoveOriginalDispatchIdentity.Digest(request.CommandId, request.Envelope), token);
        await Assert.That(authenticated.Value.Reply.Error).IsNull();
        await Assert.That(authenticated.Value.Reply.SafeDetail).IsNull();
        await ControlledPartitionMovementNativeOuterBudget.AssertAsync(response,
            source.Database.Limits.MaxBatchBytes, token);
        return GrainNativePayload.Read<GrainValue>(authenticated.Value.Reply.Payload).Value as T
            ?? throw new InvalidOperationException(typeof(T).FullName);
    }
}
