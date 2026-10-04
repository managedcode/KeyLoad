namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlRequestValidation
{
    internal static bool Prepare(CachePrepareRequest request)
        => CacheControlValidation.Header(request.Header, CacheControlOperation.Prepare)
            && CacheControlValidation.Address(request.ExpectedTargetSiloAddress);

    internal static bool Revoke(CacheRevokeRequest request)
        => CacheControlValidation.Header(request.Header, CacheControlOperation.Revoke)
            && request.GrantId != Guid.Empty && CacheControlValidation.Binding(request.TargetBinding)
            && request.TargetBinding.Slot == request.Header.TargetSlot;

    internal static bool Refresh(CacheRefreshHint request)
        => CacheControlValidation.Header(request.Header, CacheControlOperation.Refresh)
            && CacheControlValidation.Binding(request.SenderBinding)
            && request.SenderBinding.Slot == request.Header.OriginSlot;

    internal static bool Grant(CacheGrantRequest request)
    {
        if (!CacheControlValidation.Header(request.Header, CacheControlOperation.Grant)
            || request.GrantId == Guid.Empty || !CacheControlValidation.Binding(request.TargetBinding)
            || !SlotProof(request.Header, request.Slot0Proof, CacheVoterSlot.Slot0)
            || !SlotProof(request.Header, request.Slot1Proof, CacheVoterSlot.Slot1)
            || !SlotProof(request.Header, request.Slot2Proof, CacheVoterSlot.Slot2))
        {
            return false;
        }

        var first = request.Slot0Proof.Binding;
        var second = request.Slot1Proof.Binding;
        var third = request.Slot2Proof.Binding;
        var target = request.Header.TargetSlot switch
        {
            CacheVoterSlot.Slot0 => first,
            CacheVoterSlot.Slot1 => second,
            CacheVoterSlot.Slot2 => third,
            _ => null
        };
        return request.TargetBinding == target && first.Incarnation == second.Incarnation
            && first.Incarnation == third.Incarnation && Distinct(first, second)
            && Distinct(first, third) && Distinct(second, third);
    }

    private static bool SlotProof(CacheControlHeader header, CacheReadyProof? proof, CacheVoterSlot slot)
        => CacheControlValidation.Proof(proof) && proof!.Binding.Slot == slot
            && CacheControlValidation.SameRound(header, proof);

    private static bool Distinct(CachePhysicalBinding left, CachePhysicalBinding right)
        => left.NodeId != right.NodeId && !string.Equals(left.SiloAddress, right.SiloAddress, StringComparison.Ordinal);
}
