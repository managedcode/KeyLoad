namespace KeyLoad.Orleans.Features.ResourceExecution;

internal static class CacheControlReplyValidation
{
    internal static bool Prepare(CachePrepareReply reply)
    {
        if (reply.Correlation is null)
        {
            return Flat(reply) && reply.Proof is null;
        }

        if (!CacheControlValidation.Correlation(reply.Correlation, CacheControlOperation.Prepare)
            || !PrepareStatus(reply.Status))
        {
            return false;
        }

        if (reply.Status != CacheControlStatus.Ready)
        {
            return reply.Proof is null;
        }

        return CacheControlValidation.Proof(reply.Proof)
            && CacheControlValidation.SameRound(reply.Correlation.Header, reply.Proof!)
            && reply.Proof!.Binding.Slot == reply.Correlation.Header.TargetSlot;
    }

    internal static bool Grant(CacheGrantReply reply)
    {
        if (reply.Correlation is null)
        {
            return Flat(reply) && reply.GrantId == Guid.Empty
                && reply.AcceptedBinding is null && reply.AcceptedSequence == 0;
        }

        if (!CacheControlValidation.Correlation(reply.Correlation, CacheControlOperation.Grant)
            || reply.GrantId == Guid.Empty || !GrantStatus(reply.Status))
        {
            return false;
        }

        if (reply.Status is CacheControlStatus.AcceptedActive or CacheControlStatus.AcceptedCold)
        {
            return CacheControlValidation.Binding(reply.AcceptedBinding) && reply.AcceptedSequence > 0
                && reply.AcceptedBinding!.Slot == reply.Correlation.Header.TargetSlot;
        }

        return reply.AcceptedBinding is null && reply.AcceptedSequence == 0;
    }

    internal static bool Revoke(CacheRevokeReply reply)
    {
        if (reply.Correlation is null)
        {
            return Flat(reply) && reply.GrantId == Guid.Empty && reply.Effect == CacheRevokeEffect.None;
        }

        if (!CacheControlValidation.Correlation(reply.Correlation, CacheControlOperation.Revoke)
            || reply.GrantId == Guid.Empty || !RevokeStatus(reply.Status))
        {
            return false;
        }

        return reply.Status == CacheControlStatus.Revoked
            ? reply.Effect is CacheRevokeEffect.PendingRemoved or CacheRevokeEffect.LeaseWithdrawn or CacheRevokeEffect.Both
            : reply.Effect == CacheRevokeEffect.None;
    }

    internal static bool Refresh(CacheRefreshReceipt reply)
    {
        if (reply.Correlation is null)
        {
            return Flat(reply) && EmptyCoordinator(reply);
        }

        if (!CacheControlValidation.Correlation(reply.Correlation, CacheControlOperation.Refresh)
            || !RefreshStatus(reply.Status))
        {
            return false;
        }

        return reply.Status == CacheControlStatus.HintAcknowledged
            ? reply.ActualCoordinatorSessionId != Guid.Empty && CacheControlValidation.Address(reply.ActualCoordinatorSiloAddress)
            : EmptyCoordinator(reply);
    }

    private static bool Flat(ICacheControlReply reply)
        => reply.Mac.FixedTimeEquals(default) && (reply switch
        {
            CachePrepareReply value => value.Status == CacheControlStatus.Rejected,
            CacheGrantReply value => value.Status == CacheControlStatus.Rejected,
            CacheRevokeReply value => value.Status == CacheControlStatus.Rejected,
            CacheRefreshReceipt value => value.Status == CacheControlStatus.Rejected,
            _ => false
        });

    private static bool EmptyCoordinator(CacheRefreshReceipt reply)
        => reply.ActualCoordinatorSessionId == Guid.Empty && reply.CurrentRoundNonce == Guid.Empty
            && reply.ActualCoordinatorSiloAddress is null;

    private static bool CommonStatus(CacheControlStatus status)
        => status is CacheControlStatus.PolicyMismatch or CacheControlStatus.Replay
            or CacheControlStatus.Rejected or CacheControlStatus.Closed or CacheControlStatus.Capacity;

    private static bool PrepareStatus(CacheControlStatus status)
        => CommonStatus(status) || status is CacheControlStatus.Ready or CacheControlStatus.Busy or CacheControlStatus.NotReady;

    private static bool GrantStatus(CacheControlStatus status)
        => CommonStatus(status) || status is CacheControlStatus.AcceptedActive or CacheControlStatus.AcceptedCold
            or CacheControlStatus.Busy or CacheControlStatus.NotReady or CacheControlStatus.StaleChallenge;

    private static bool RevokeStatus(CacheControlStatus status)
        => CommonStatus(status) || status is CacheControlStatus.Revoked or CacheControlStatus.StaleChallenge;

    private static bool RefreshStatus(CacheControlStatus status)
        => CommonStatus(status) || status is CacheControlStatus.HintAcknowledged or CacheControlStatus.Busy or CacheControlStatus.NotReady;
}
