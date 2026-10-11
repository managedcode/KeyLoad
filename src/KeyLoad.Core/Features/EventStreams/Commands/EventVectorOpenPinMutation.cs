namespace KeyLoad.Core;

internal static class EventVectorOpenPinMutation
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const string NonceFormat = "N";

    internal static EventVectorSourceMutation Create(EventVectorMap map, EventVectorEntry entry,
        PhysicalShardRecord sourceOwner, PhysicalShardRecord controlOwner, DateTimeOffset originalExpiry)
        => new()
        {
            Version = Version,
            MapId = map.MapId,
            ControlPartition = map.ControlPartition,
            ControlIncarnation = map.ControlIncarnation,
            Source = entry.Source,
            PrincipalId = map.PrincipalId,
            SourceOwner = sourceOwner,
            ControlOwner = controlOwner,
            Role = EventVectorSourcePhaseRole.Pin,
            ExpectedPinRevision = InitialRevision,
            ExpectedCoverageGeneration = InitialRevision,
            Position = entry.Position,
            CoverageGeneration = map.CoverageGeneration,
            SourcePolicyEpoch = entry.PolicyEpoch,
            SourceSchemaVersion = entry.SchemaVersion,
            LogicalPlacementRevision = entry.LogicalPlacementRevision,
            DirectoryFence = entry.DirectoryFence,
            Nonce = Guid.NewGuid().ToString(NonceFormat),
            FirstExpiresAt = originalExpiry,
            CleanupGeneration = EventVectorSourcePhaseIds.InitialCleanupGeneration
        };
}
