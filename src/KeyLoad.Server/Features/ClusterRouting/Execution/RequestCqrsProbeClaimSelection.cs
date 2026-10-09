using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Selects the one unclaimed private arm that matches the validated request identity.</summary>
internal static class RequestCqrsProbeClaimSelection
{
    internal static RequestCqrsProbeLoadedArm? Find(GrainRequestProbeIdentity identity, RequestCqrsProbeSnapshot snapshot, string actualVoter)
    {
        const int EmptyMatchesLength = 0;
        const int ClaimEmptyMatchesLength = 1;
        const int MatchesFirstIndex = 0;

        var matches = snapshot.Arms.Where(loaded => loaded.Record.PrincipalId == identity.PrincipalId
            && loaded.Record.CommandId == identity.CommandId
            && (loaded.Record.TargetVoter is null || loaded.Record.TargetVoter == actualVoter)
            && loaded.Record.ReadKind == identity.ReadKind && identity.RequestId != Guid.Empty
            && loaded.Record.Phase != RequestCqrsProbePhase.CanonicalJournalFlushed
            && !snapshot.Markers.Any(marker => marker.ArmId == loaded.Record.ArmId)).ToArray();
        if (matches.Length == EmptyMatchesLength)
        { return null; }
        if (matches.Length != ClaimEmptyMatchesLength)
        { throw Invalid(); }
        return matches[MatchesFirstIndex];
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
