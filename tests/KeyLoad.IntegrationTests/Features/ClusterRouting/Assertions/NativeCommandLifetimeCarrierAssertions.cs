using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCommandLifetimeCarrierAssertions
{
    private const int MissingStep = 1;
    private const int SubstitutedStep = 2;
    private const int ForeignStep = 3;
    private const int ChangedCharacterCount = 1;

    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementLifetimeProofRf3Original original,
        PartitionMovementLifetimeProofRf3Original? donor, RequestCqrsProbeMarkerRecord marker,
        RequestCqrsControlledLifetimeStepWitness witness, ReplicaSiloDiscovery[] owners)
    {
        await Assert.That(witness.SessionId).IsEqualTo(wave.QueryControls.SessionId);
        await Assert.That(witness.ArmId).IsEqualTo(original.ArmId);
        await Assert.That(witness.RequestId).IsEqualTo(marker.RequestId);
        await Assert.That(witness.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(witness.PrincipalId).IsEqualTo(PartitionMovementPublicParentRf3Administrator.PrincipalId);
        await Assert.That(witness.Partition.ToPartition()).IsEqualTo(original.Command.Partition);
        await Assert.That(witness.TechnicalPrincipalId).IsEqualTo(PartitionStoreProtocol.AdministratorId);
        await Assert.That(witness.ConnectionId != Guid.Empty).IsTrue();
        await Assert.That(witness.SourceArmId).IsEqualTo(donor?.ArmId);
        await Assert.That(witness.CallerVoter).IsEqualTo(marker.Voter);
        await Assert.That(witness.CallerSiloAddress).IsEqualTo(owners.Single(owner => owner.VoterId == witness.CallerVoter).SiloAddress);
        await Assert.That(witness.ReceiverSiloAddress).IsEqualTo(owners.Single(owner => owner.VoterId == witness.ReceiverVoter).SiloAddress);
        var carrierBytes = NativeCommandLifetimeStepFiles.Parts(wave, witness, P.CarrierRole, P.BaselineStep,
            witness.CarrierBytes, witness.CarrierParts, witness.CarrierSha256);
        var carrier = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(carrierBytes);
        var request = carrier.OriginalRequest;
        await Assert.That(request.CommandId).IsEqualTo(witness.EffectCommandId);
        await Assert.That(request.Envelope.Stage).IsEqualTo(PartitionMovePeerStage.ControlApplyCommand);
        await Assert.That(request.Envelope.Partition).IsEqualTo(original.Command.Partition);
        var logical = carrier.Proof.ControlProof.OriginalDelegation.OriginalOperation!;
        await Assert.That(logical.Id).IsEqualTo(original.Command.CommandId);
        await Assert.That(logical.PrincipalId).IsEqualTo(witness.PrincipalId);
        await SqlRf3Protocol.EqualAsync(original.Command, NativeCommandPayload.Read<CommandRequest>(logical));
        if (witness.Step == P.BaselineStep)
        {
            await Assert.That(original.Baseline).IsNull();
            original.Baseline = witness;
            original.Carrier = carrierBytes;
        }
        else
        {
            await Assert.That(witness.ConnectionId).IsEqualTo(original.Baseline!.ConnectionId);
            await Assert.That(witness.EffectCommandId).IsEqualTo(original.Baseline.EffectCommandId);
            await Assert.That(carrierBytes.SequenceEqual(original.Carrier!)).IsTrue();
            await NegativeAsync(wave, original, witness, carrier, donor);
        }
        await Assert.That(original.Producer.IsCompleted).IsFalse();
    }

    private static async Task NegativeAsync(TwoRf3MembershipWave wave, PartitionMovementLifetimeProofRf3Original state,
        RequestCqrsControlledLifetimeStepWitness witness,
        PartitionControlLifetimeTransportV2 original, PartitionMovementLifetimeProofRf3Original? donor)
    {
        var requestBytes = NativeCommandLifetimeStepFiles.Parts(wave, witness, P.RequestRole, witness.Step,
            witness.RequestBytes, witness.RequestParts, witness.RequestSha256);
        var replyBytes = NativeCommandLifetimeStepFiles.Parts(wave, witness, P.ReplyRole, witness.Step,
            witness.ReplyBytes, witness.ReplyParts, witness.ReplySha256);
        var request = NativeSerialization.Deserialize<PartitionMovementGrainServiceRequest>(requestBytes);
        var reply = NativeSerialization.Deserialize<PartitionMovementGrainServiceReply>(replyBytes);
        await Assert.That(request.Query).IsFalse();
        await Assert.That(request.CallerVoter).IsEqualTo(witness.CallerVoter);
        await Assert.That(request.CallerSiloAddress).IsEqualTo(witness.CallerSiloAddress);
        await Assert.That(request.DestinationVoter).IsEqualTo(witness.ReceiverVoter);
        await Assert.That(request.DestinationSiloAddress).IsEqualTo(witness.ReceiverSiloAddress);
        await Assert.That(reply.Nonce).IsEqualTo(request.Nonce);
        var variation = witness.Step switch
        {
            MissingStep => ControlledLifetimeReplayVariation.Missing,
            SubstitutedStep => ControlledLifetimeReplayVariation.Substituted,
            ForeignStep => ControlledLifetimeReplayVariation.Foreign,
            _ => throw new InvalidDataException(P.Invalid)
        };
        var expected = variation switch
        {
            ControlledLifetimeReplayVariation.Missing => ErrorCode.Unauthenticated,
            ControlledLifetimeReplayVariation.Substituted => ErrorCode.Corruption,
            _ => ErrorCode.OwnershipLost
        };
        await Assert.That(witness.Variation).IsEqualTo(variation);
        await Assert.That(witness.Error).IsEqualTo(expected);
        await Assert.That(reply.Error).IsEqualTo(expected);
        await VerifyNativeMacsAsync(wave, request, reply);
        if (variation == ControlledLifetimeReplayVariation.Missing)
        {
            await Assert.That(request.Channel).IsEqualTo(PartitionMovementGrainServiceChannel.Phase);
            await RequestAsync(state, NativeSerialization.Serialize(original.OriginalRequest), request.Payload);
            return;
        }
        await Assert.That(request.Channel).IsEqualTo(PartitionMovementGrainServiceChannel.ControlledLifetime);
        var changed = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(request.Payload.Span);
        await RequestAsync(state, NativeSerialization.Serialize(original.OriginalRequest),
            NativeSerialization.Serialize(changed.OriginalRequest));
        if (variation == ControlledLifetimeReplayVariation.Foreign)
        {
            await Assert.That(donor!.Producer.IsCompletedSuccessfully).IsTrue();
            await Assert.That(donor.Receipt).IsNotNull();
            var genuineDonor = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(donor.Carrier!);
            await BytesAsync(NativeSerialization.Serialize(genuineDonor.Proof), NativeSerialization.Serialize(changed.Proof));
            await Assert.That(changed.Proof.OriginalGrant.GrantId).IsNotEqualTo(original.Proof.OriginalGrant.GrantId);
            return;
        }
        await SubstitutedAsync(original, changed);
    }

    private static async Task RequestAsync(PartitionMovementLifetimeProofRf3Original state,
        ReadOnlyMemory<byte> before, ReadOnlyMemory<byte> after)
    {
        var original = NativeSerialization.Deserialize<PartitionMovementTransportRequest>(before.Span);
        var actual = NativeSerialization.Deserialize<PartitionMovementTransportRequest>(after.Span);
        await Assert.That(actual.Envelope.Nonce != Guid.Empty).IsTrue();
        await Assert.That(actual.Envelope.Nonce).IsNotEqualTo(original.Envelope.Nonce);
        await Assert.That(state.NegativeNonces.Add(actual.Envelope.Nonce)).IsTrue();
        var expected = original with
        {
            Envelope = original.Envelope with
            {
                Nonce = actual.Envelope.Nonce
            }
        };
        await BytesAsync(NativeSerialization.Serialize(expected), after);
    }

    private static async Task SubstitutedAsync(PartitionControlLifetimeTransportV2 original, PartitionControlLifetimeTransportV2 changed)
    {
        var coverage = original.Proof.ControlProof.Coverage;
        var first = coverage.Lifetimes[P.FirstIndex];
        var actual = changed.Proof.ControlProof.Coverage.Lifetimes[P.FirstIndex];
        await Assert.That(actual.Incarnation.Value.Length).IsEqualTo(first.Incarnation.Value.Length);
        await Assert.That(actual.Incarnation.Value.All(char.IsAsciiHexDigitLower)).IsTrue();
        await Assert.That(actual.Incarnation.Value.Zip(first.Incarnation.Value).Count(pair => pair.First != pair.Second))
            .IsEqualTo(ChangedCharacterCount);
        var expected = original.Proof with
        {
            ControlProof = original.Proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = coverage.Lifetimes.SetItem(P.FirstIndex, first with
                    {
                        Incarnation = actual.Incarnation
                    })
                }
            }
        };
        await BytesAsync(NativeSerialization.Serialize(expected), NativeSerialization.Serialize(changed.Proof));
    }

    private static async Task VerifyNativeMacsAsync(TwoRf3MembershipWave wave,
        PartitionMovementGrainServiceRequest request, PartitionMovementGrainServiceReply reply)
    {
        var source = Convert.FromBase64String(wave.Profile.PeerSecret);
        try
        {
            using var sourceMac = new PartitionMovementMac(source, IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes);
            await Assert.That(sourceMac.VerifyNativeRequest(request)).IsTrue();
            await Assert.That(sourceMac.VerifyNativeReply(reply)).IsTrue();
            await Assert.That(request.Channel == PartitionMovementGrainServiceChannel.Phase
                ? sourceMac.Verify(request.Payload.Span, request.Signature, reply: false)
                : sourceMac.VerifyControlledLifetime(request.Payload.Span, request.Signature)).IsTrue();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(source);
        }
    }

    private static async Task BytesAsync(ReadOnlyMemory<byte> expected, ReadOnlyMemory<byte> actual)
        => await Assert.That(actual.Span.SequenceEqual(expected.Span)).IsTrue();
}
