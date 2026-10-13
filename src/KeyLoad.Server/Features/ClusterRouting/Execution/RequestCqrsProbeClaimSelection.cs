using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Selects the one unclaimed private arm that matches the validated request identity.</summary>
internal static class RequestCqrsProbeClaimSelection
{
    internal static RequestCqrsProbeClaim? Bind(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        RequestCqrsProbeSnapshot snapshot, string actualVoter, RequestCqrsProbeLifecycle lifecycle, bool knownPhase)
    {
        if (!knownPhase)
        { throw Invalid(); }
        if (global::Orleans.Runtime.RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState context
            || context.RequestId != identity.RequestId || context.CommandId != identity.CommandId || context.ConnectionId == Guid.Empty)
        { throw Invalid(); }
        var existing = lifecycle.FindClaim(identity.RequestId);
        if (existing is not null)
        {
            if (existing.Identity != identity || existing.ConnectionId != context.ConnectionId)
            { throw Invalid(); }
            return existing;
        }
        var selected = Find(identity, snapshot, actualVoter, phase);
        if (selected is null)
        { return null; }
        // Retain the original validated identity for the joined producer-disposal relay.
        return lifecycle.AddClaim(identity, selected, context.ConnectionId);
    }

    internal static RequestCqrsProbeLoadedArm? Find(GrainRequestProbeIdentity identity, RequestCqrsProbeSnapshot snapshot, string actualVoter, GrainRequestPhase phase)
    {
        const int EmptyMatchesLength = 0;
        const int ClaimEmptyMatchesLength = 1;
        const int MatchesFirstIndex = 0;

        var matches = snapshot.Arms.Where(loaded => loaded.Record.PrincipalId == identity.PrincipalId
            && loaded.Record.CommandId == identity.CommandId
            && (loaded.Record.TargetVoter is null || loaded.Record.TargetVoter == actualVoter)
            && loaded.Record.ReadKind == identity.ReadKind && identity.RequestId != Guid.Empty
            && (loaded.Record.Phase != RequestCqrsProbePhase.SubmitReturned || loaded.Record.Partition is null
                || identity.CommandKind == OperationKind.PartitionMovementPhase)
            && RequestCqrsEventVectorProbePair.Eligible(loaded.Record, identity, snapshot.Markers)
            && (loaded.Record.Phase != RequestCqrsProbePhase.OnlineTextCaptured
                || identity.CommandKind == OperationKind.MaintainOnlineTextIndex)
            && loaded.Record.Phase != RequestCqrsProbePhase.CanonicalJournalFlushed
            && loaded.Record.Phase != RequestCqrsProbePhase.ReplicaEntryAcknowledged
            && !(loaded.Record.Phase == RequestCqrsProbePhase.ParentReceiverIssueObserved
                && loaded.Record.SourceArmId is not null)
            && !snapshot.Markers.Any(marker => marker.ArmId == loaded.Record.ArmId)).ToArray();
        if (matches.Length == EmptyMatchesLength)
        { return null; }
        if (matches.Length != ClaimEmptyMatchesLength)
        { return RequestCqrsSampleChunkProbePair.Select(matches, phase); }
        return matches[MatchesFirstIndex];
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
