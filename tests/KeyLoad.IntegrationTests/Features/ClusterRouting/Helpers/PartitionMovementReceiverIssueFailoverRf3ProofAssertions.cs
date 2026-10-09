using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>The actual retained signed reply, canonical proof ACK and receiver own outcome establish the read voter.</summary>
internal static class PartitionMovementReceiverIssueFailoverRf3ProofAssertions
{
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3NativeCut[] cuts,
        PartitionMoveParentPhase original, ReplicaSiloDiscovery? expectedDiscovery)
    {
        var witness = original.OriginalReceiverIssuanceWitness
            ?? throw new InvalidOperationException(PartitionMovementProtocol.InvalidProof);
        await Assert.That(original.ReceiverIssuanceCheckpointReceipt).IsNotNull();
        await Assert.That(witness.OriginalPhaseCommandId).IsEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(witness.QueryNonce).IsNotEqualTo(original.OriginalRequestNonce);
        var reply = NativeSerialization.Deserialize<PartitionMovementTransportReply>(witness.OriginalReplyBytes.Span);
        await Assert.That(reply.Reply.Error).IsNull();
        await Assert.That(reply.CommandId).IsEqualTo(original.OriginalPhaseCommandId);
        await Assert.That(reply.Nonce).IsEqualTo(witness.QueryNonce);
        await Assert.That(reply.OriginalPhaseIdentityDigest).IsEqualTo(original.OriginalPhaseIdentityDigest);
        if (expectedDiscovery is not null)
        { await SqlRf3Protocol.EqualAsync(expectedDiscovery, reply.Discovery); }
        var result = GrainNativePayload.Read<GrainValue>(reply.Reply.Payload).Value as PartitionMovementReceiverIssuanceResult
            ?? throw new InvalidOperationException(PartitionMovementProtocol.InvalidProof);
        var issued = result.Snapshot.Issuance;
        await Assert.That(issued.OriginalRequestNonce).IsEqualTo(original.OriginalRequestNonce);
        await Assert.That(issued.OriginalExpiresAt).IsEqualTo(original.OriginalExpiresAt);
        await Assert.That(issued.OriginalGrantId).IsEqualTo(original.OriginalGrant!.GrantId);
        await Assert.That(issued.OriginalPhaseIdentityDigest).IsEqualTo(original.OriginalPhaseIdentityDigest);
        await Assert.That(result.Snapshot.OriginalResult.Error).IsNull();
        var actual = result.Snapshot.OriginalResult.Get<PartitionMovePhaseResult>();
        await Assert.That(actual.Stage).IsEqualTo(PartitionMovePeerStage.ReceiverIssue);
        await Assert.That(actual.Journal.CommandId).IsEqualTo(issued.IssuanceCommandId);
        await Assert.That(actual.Journal.AppliedPosition).IsEqualTo(issued.IssuanceAppliedPosition);
        await Assert.That(result.Snapshot.CurrentReadCut).IsGreaterThanOrEqualTo(issued.IssuanceAppliedPosition);
        await SqlRf3Protocol.EqualAsync(issued, actual.ReceiverIssuance);
        foreach (var receiver in cuts.Where(cut => cut.OriginalReceiverIssuance is not null))
        {
            await SqlRf3Protocol.EqualAsync(issued, receiver.OriginalReceiverIssuance);
            var own = PartitionMovementCapturePointerRf3Fault.Read<StoredOutcome>(receiver,
                KeySpace.PartitionOutcome(issued.Partition, issued.ReceiverPrincipalId, issued.IssuanceCommandId));
            await Assert.That(own).IsNotNull();
            await SqlRf3Protocol.EqualAsync(result.Snapshot.OriginalResult, own!.Result);
        }
        await Assert.That(cuts.Count(cut => cut.OriginalReceiverIssuance is not null))
            .IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
    }
}
