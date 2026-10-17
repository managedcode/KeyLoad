namespace KeyLoad.Core.Features.ClusterRouting.Contracts;

/// <summary>Defines the canonical native physical-shard catalog identity.</summary>
internal static class PhysicalShardCatalogProtocol
{
    internal const int CurrentVersion = 1;
    internal const int MaximumEncodedBytes = 8192;
    internal const long InitialRevision = 1;
    internal const long InitialPlacementEpoch = 1;
    internal const int MaximumVoters = 3;
    internal const int MaximumVoterIdBytes = 512;
}
