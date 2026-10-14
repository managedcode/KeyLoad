namespace KeyLoad.Core;

internal static class EventVectorPhasePreparation
{
    private const int Version = 1;
    private const int FirstOrdinal = 0;
    private const int IncludedOrdinal = 1;
    private const long NextCoverage = 1;
    private const string InvalidPhase = "The intended event vector source phase is inconsistent.";

    internal static EventVectorSourcePhase Prepare(EventVectorMap map, int sourceOrdinal,
        EventVectorEntry originalEntry, EventVectorSourceMutation mutation, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(originalEntry);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(admission);
        if (map.Version != Version || mutation.Version != Version || map.Released
            || mutation.MapId != map.MapId || mutation.ControlPartition != map.ControlPartition
            || mutation.ControlIncarnation != map.ControlIncarnation || mutation.PrincipalId != map.PrincipalId
            || originalEntry.Source != mutation.Source || originalEntry.PrincipalId != map.PrincipalId
            || mutation.Position < originalEntry.Position
            || sourceOrdinal < FirstOrdinal || !ValidRole(map.State, mutation.Role)
            || mutation.CleanupGeneration != (mutation.Role == EventVectorSourcePhaseRole.Release
                ? map.CleanupGeneration : EventVectorSourcePhaseIds.InitialCleanupGeneration)
            || !ValidCoverage(map, mutation))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidPhase);
        }
        admission.RequireEntryCount(checked(sourceOrdinal + IncludedOrdinal));
        var phase = new EventVectorSourcePhase
        {
            Version = Version,
            MapId = map.MapId,
            ControlPartition = map.ControlPartition,
            ControlIncarnation = map.ControlIncarnation,
            MapRevision = map.Revision,
            CoverageGeneration = mutation.CoverageGeneration,
            Role = mutation.Role,
            SourceOrdinal = sourceOrdinal,
            Source = mutation.Source,
            SourceOwner = mutation.SourceOwner,
            ControlOwner = mutation.ControlOwner,
            PrincipalId = map.PrincipalId,
            ControlPolicyEpoch = map.ControlPolicyEpoch,
            SourcePolicyEpoch = mutation.SourcePolicyEpoch,
            SourceSchemaVersion = mutation.SourceSchemaVersion,
            OriginalPosition = originalEntry.Position,
            TargetPosition = mutation.Position,
            Nonce = mutation.Nonce,
            FirstExpiresAt = mutation.FirstExpiresAt,
            Disposition = EventVectorSourcePhaseDisposition.Admitted,
            SourceLogicalPlacementRevision = mutation.LogicalPlacementRevision,
            SourceDirectoryFence = mutation.DirectoryFence,
            CleanupGeneration = mutation.CleanupGeneration
        };
        return EventVectorSourcePhaseEncoding.Encode(phase, mutation, admission);
    }

    private static bool ValidCoverage(EventVectorMap map, EventVectorSourceMutation mutation)
        => mutation.CoverageGeneration == map.CoverageGeneration
            || map.State == EventVectorMapState.Refreshing
                && mutation.CoverageGeneration == checked(map.CoverageGeneration + NextCoverage);

    private static bool ValidRole(EventVectorMapState state, EventVectorSourcePhaseRole role)
        => state switch
        {
            EventVectorMapState.Opening => role == EventVectorSourcePhaseRole.Pin,
            EventVectorMapState.Acknowledging => role == EventVectorSourcePhaseRole.Advance,
            EventVectorMapState.Refreshing => role is EventVectorSourcePhaseRole.Pin
                or EventVectorSourcePhaseRole.Advance or EventVectorSourcePhaseRole.Release,
            EventVectorMapState.Releasing => role == EventVectorSourcePhaseRole.Release,
            _ => false
        };
}
