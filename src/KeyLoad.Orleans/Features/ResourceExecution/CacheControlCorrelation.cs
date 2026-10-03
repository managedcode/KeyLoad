using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlCorrelation
{
    internal static bool TryCreate(ICacheControlRequest? request, out CacheReplyCorrelation? correlation)
    {
        correlation = null;
        if (!CacheControlMeasurement.TryGet(request, out var measurement) || request!.Mac.FixedTimeEquals(default)
            || !CacheControlWire.TryEncodeSigned(request, out var complete))
        {
            return false;
        }

        var input = new byte[measurement.CorrelationBytes];
        var writer = new CacheControlWriter(input);
        writer.String(CacheControlNames.CorrelationPurpose);
        writer.Byte(CacheControlMessageEncoding.Opcode(request));
        writer.Bytes(complete);
        Span<byte> digest = stackalloc byte[CacheControlDigest.ByteLength];
        _ = SHA256.HashData(input, digest);
        correlation = new(request.Header, CacheControlDigest.FromBytes(digest));
        return true;
    }

    internal static bool TryMatch(ICacheControlRequest? request, ICacheControlReply? reply)
    {
        if (!CacheControlMeasurement.TryGet(request, out _) || !CacheControlMeasurement.TryGet(reply, out _)
            || request!.Mac.FixedTimeEquals(default) || reply!.Mac.FixedTimeEquals(default) || reply.Correlation is not { } actual
            || !TryCreate(request, out var expected) || !CacheControlHeaderComparison.Exact(actual.Header, expected!.Header)
            || !actual.SignedRequestDigest.FixedTimeEquals(expected.SignedRequestDigest))
        {
            return false;
        }

        return (request, reply) switch
        {
            (CachePrepareRequest original, CachePrepareReply response) => Prepare(original, response),
            (CacheGrantRequest original, CacheGrantReply response) => Grant(original, response),
            (CacheRevokeRequest original, CacheRevokeReply response) => original.GrantId == response.GrantId,
            (CacheRefreshHint, CacheRefreshReceipt) => true,
            _ => false
        };
    }

    private static bool Prepare(CachePrepareRequest request, CachePrepareReply reply)
        => reply.Status != CacheControlStatus.Ready
            || string.Equals(request.ExpectedTargetSiloAddress, reply.Proof!.Binding.SiloAddress, StringComparison.Ordinal);

    private static bool Grant(CacheGrantRequest request, CacheGrantReply reply)
    {
        if (request.GrantId != reply.GrantId)
        {
            return false;
        }

        if (reply.Status is not (CacheControlStatus.AcceptedActive or CacheControlStatus.AcceptedCold))
        {
            return true;
        }

        var sequence = request.Header.TargetSlot switch
        {
            CacheVoterSlot.Slot0 => request.Slot0Proof.ChallengeSequence,
            CacheVoterSlot.Slot1 => request.Slot1Proof.ChallengeSequence,
            CacheVoterSlot.Slot2 => request.Slot2Proof.ChallengeSequence,
            _ => 0
        };
        return reply.AcceptedBinding == request.TargetBinding && reply.AcceptedSequence == sequence;
    }
}
