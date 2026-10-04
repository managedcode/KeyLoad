using System.Text;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlValidation
{
    internal static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaximumAddressCharacters = 256;
    private const int MaximumAddressBytes = 1024;
    private const long MinimumUnixMilliseconds = -62_135_596_800_000;
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;

    internal static bool Slot(CacheVoterSlot slot)
        => slot is CacheVoterSlot.Slot0 or CacheVoterSlot.Slot1 or CacheVoterSlot.Slot2;

    internal static bool Address(string? address)
    {
        if (address is null || address.Length > MaximumAddressCharacters)
        {
            return false;
        }

        try
        {
            return StrictUtf8.GetByteCount(address) <= MaximumAddressBytes
                && SiloAddress.TryParse(address, out var parsed)
                && string.Equals(address, parsed.ToParsableString(), StringComparison.Ordinal);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    internal static bool Binding(CachePhysicalBinding? binding)
        => binding is not null && Slot(binding.Slot)
            && binding.NodeId != Guid.Empty && binding.Incarnation != Guid.Empty
            && binding.RuntimeId != Guid.Empty && binding.Role == CachePhysicalRole.Canonical
            && Address(binding.SiloAddress);

    internal static bool Header(CacheControlHeader? header, CacheControlOperation operation)
    {
        if (header is null || header.Version != 1 || header.Operation != operation
            || header.ScopeHash.FixedTimeEquals(default) || header.PolicyHash.FixedTimeEquals(default) || header.PolicyRevision <= 0
            || !Slot(header.OriginSlot) || header.RequestNonce == Guid.Empty
            || header.SentUnixMilliseconds is < MinimumUnixMilliseconds or > MaximumUnixMilliseconds)
        {
            return false;
        }

        if (operation == CacheControlOperation.Refresh)
        {
            return header.TargetSlot is null
                && (header.CoordinatorSessionId != Guid.Empty || header.RoundNonce == Guid.Empty);
        }

        return header.TargetSlot is { } target && Slot(target)
            && header.CoordinatorSessionId != Guid.Empty && header.RoundNonce != Guid.Empty;
    }

    internal static bool Proof(CacheReadyProof? proof)
        => proof is not null && proof.Version == 1
            && !proof.ScopeHash.FixedTimeEquals(default) && !proof.PolicyHash.FixedTimeEquals(default) && proof.PolicyRevision > 0
            && Slot(proof.OriginSlot) && proof.CoordinatorSessionId != Guid.Empty
            && proof.RoundNonce != Guid.Empty && proof.ChallengeId != Guid.Empty
            && proof.ChallengeSequence > 0 && proof.Status == CacheControlStatus.Ready
            && Binding(proof.Binding);

    internal static bool SameRound(CacheControlHeader header, CacheReadyProof proof)
        => proof.ScopeHash.FixedTimeEquals(header.ScopeHash) && proof.PolicyHash.FixedTimeEquals(header.PolicyHash)
            && proof.PolicyRevision == header.PolicyRevision && proof.OriginSlot == header.OriginSlot
            && proof.CoordinatorSessionId == header.CoordinatorSessionId && proof.RoundNonce == header.RoundNonce;

    internal static bool Correlation(CacheReplyCorrelation? correlation, CacheControlOperation operation)
        => correlation is not null && !correlation.SignedRequestDigest.FixedTimeEquals(default)
            && Header(correlation.Header, operation);
}
