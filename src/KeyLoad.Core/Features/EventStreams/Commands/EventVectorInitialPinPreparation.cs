namespace KeyLoad.Core;

internal static class EventVectorInitialPinPreparation
{
    private const int Version = 1;
    private const string NonceFormat = "N";
    private const int FirstOrdinal = 0;
    private const long Initial = 0;
    private const long FirstRevision = 1;

    internal static EventVectorSourcePhase Prepare(EventVectorFirstIssuance issuance,
        PrincipalRecord principal, PhysicalShardRecord controlOwner, EventVectorEntry entry,
        PhysicalShardRecord sourceOwner, EventVectorAdmissionPolicy admission, ReadExecutionBudget work)
    {
        var nonce = Guid.NewGuid().ToString(NonceFormat);
        var mutation = new EventVectorSourceMutation
        {
            Version = Version,
            MapId = issuance.MapId,
            ControlPartition = issuance.ControlPartition,
            ControlIncarnation = controlOwner.Incarnation,
            Source = entry.Source,
            PrincipalId = principal.Id,
            SourceOwner = sourceOwner,
            ControlOwner = controlOwner,
            Role = EventVectorSourcePhaseRole.Pin,
            ExpectedPinRevision = Initial,
            Position = entry.Position,
            CoverageGeneration = FirstRevision,
            SourcePolicyEpoch = entry.PolicyEpoch,
            SourceSchemaVersion = entry.SchemaVersion,
            LogicalPlacementRevision = entry.LogicalPlacementRevision,
            DirectoryFence = entry.DirectoryFence,
            Nonce = nonce,
            FirstExpiresAt = issuance.OriginalParentExpiresAt,
            ExpectedCoverageGeneration = Initial,
            CleanupGeneration = Initial
        };
        var phase = new EventVectorSourcePhase
        {
            Version = Version,
            MapId = issuance.MapId,
            ControlPartition = issuance.ControlPartition,
            ControlIncarnation = controlOwner.Incarnation,
            MapRevision = FirstRevision,
            CoverageGeneration = FirstRevision,
            Role = EventVectorSourcePhaseRole.Pin,
            SourceOrdinal = FirstOrdinal,
            Source = entry.Source,
            SourceOwner = sourceOwner,
            ControlOwner = controlOwner,
            PrincipalId = principal.Id,
            ControlPolicyEpoch = principal.PolicyEpoch,
            SourcePolicyEpoch = entry.PolicyEpoch,
            SourceSchemaVersion = entry.SchemaVersion,
            OriginalPosition = entry.Position,
            TargetPosition = entry.Position,
            Nonce = nonce,
            FirstExpiresAt = issuance.OriginalParentExpiresAt,
            Disposition = EventVectorSourcePhaseDisposition.Admitted,
            SourceLogicalPlacementRevision = entry.LogicalPlacementRevision,
            SourceDirectoryFence = entry.DirectoryFence,
            CleanupGeneration = Initial
        };
        return EventVectorSourcePhaseEncoding.Encode(phase, mutation, admission, work);
    }
}
