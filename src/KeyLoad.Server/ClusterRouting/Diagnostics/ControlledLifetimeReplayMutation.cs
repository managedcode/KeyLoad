using KeyLoad.Core.Features.ClusterRouting.Contracts;
using static KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class ControlledLifetimeReplayMutation
{
    private const char FirstHex = '0';
    private const char AlternateHex = '1';

    internal static byte[] Encode(PartitionMovementTransportRequest original, byte[] carrier,
        ControlledLifetimeReplayVariation variation, byte[]? donor)
    {
        var request = original with
        {
            Envelope = original.Envelope with
            {
                Nonce = Guid.NewGuid()
            }
        };
        var requestBytes = NativeSerialization.Serialize(request);
        if (variation == ControlledLifetimeReplayVariation.Missing)
        {
            return requestBytes;
        }
        var source = NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(carrier);
        var proof = variation == ControlledLifetimeReplayVariation.Foreign
            ? NativeSerialization.Deserialize<PartitionControlLifetimeTransportV2>(donor ?? throw InvalidProof()).Proof
            : Substitute(source.Proof);
        return NativeSerialization.Serialize(PartitionControlLifetimeTransportV2.Create(request, proof));
    }

    internal static PartitionMoveLifetimeProofV1 Substitute(PartitionMoveLifetimeProofV1 proof)
    {
        var coverage = proof.ControlProof.Coverage;
        if (coverage.Lifetimes.IsDefaultOrEmpty)
        {
            throw InvalidProof();
        }
        var lifetime = coverage.Lifetimes[FirstIndex];
        var value = lifetime.Incarnation.Value;
        if (value is not { Length: DigestCharacters } || !value.All(char.IsAsciiHexDigitLower))
        {
            throw InvalidProof();
        }
        var changed = value.ToCharArray();
        changed[FirstIndex] = changed[FirstIndex] == FirstHex ? AlternateHex : FirstHex;
        var lifetimes = coverage.Lifetimes.SetItem(FirstIndex, lifetime with
        {
            Incarnation = lifetime.Incarnation with
            {
                Value = new string(changed)
            }
        });
        return proof with
        {
            ControlProof = proof.ControlProof with
            {
                Coverage = coverage with
                {
                    Lifetimes = lifetimes
                }
            }
        };
    }

    internal static ErrorCode Expected(ControlledLifetimeReplayVariation variation) => variation switch
    {
        ControlledLifetimeReplayVariation.Missing => ErrorCode.Unauthenticated,
        ControlledLifetimeReplayVariation.Substituted => ErrorCode.Corruption,
        ControlledLifetimeReplayVariation.Foreign => ErrorCode.OwnershipLost,
        _ => throw InvalidProof()
    };

    internal static ControlledLifetimeReplayVariation At(int step) => step switch
    {
        FirstNegativeStep => ControlledLifetimeReplayVariation.Missing,
        SubstitutedStep => ControlledLifetimeReplayVariation.Substituted,
        LastNegativeStep => ControlledLifetimeReplayVariation.Foreign,
        _ => throw InvalidProof()
    };
    private static InvalidOperationException InvalidProof() => new(Invalid);
}
