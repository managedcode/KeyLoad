using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Owns the bounded, read-only evidence required to convert a stopped replica snapshot set.</summary>
public sealed class ReplicaSnapshotUpgradePlan
{
    internal ReplicaSnapshotUpgradePlan(string sourceSnapshots,
        ImmutableArray<ReplicaSnapshotUpgradeImage> images, ReplicaUpgradeStoreBinding canonical,
        ReplicaUpgradeStoreBinding replica, ReplicaUpgradeConfigurationBinding configuration,
        ReplicaHardState hardState, string hardStateSha256, long canonicalAppliedPosition)
    {
        SourceSnapshots = sourceSnapshots;
        Images = images;
        Canonical = canonical;
        Replica = replica;
        Configuration = configuration;
        HardState = hardState;
        HardStateSha256 = hardStateSha256;
        CanonicalAppliedPosition = canonicalAppliedPosition;
    }

    internal string SourceSnapshots { get; }
    internal ImmutableArray<ReplicaSnapshotUpgradeImage> Images { get; }
    internal ReplicaUpgradeStoreBinding Canonical { get; }
    internal ReplicaUpgradeStoreBinding Replica { get; }
    internal ReplicaUpgradeConfigurationBinding Configuration { get; }
    internal ReplicaHardState HardState { get; }
    internal string HardStateSha256 { get; }
    internal long CanonicalAppliedPosition { get; }
}

internal sealed record ReplicaSnapshotUpgradeImage(string FileName, long Length, string Sha256, StorageSnapshot Snapshot);

internal sealed record ReplicaUpgradeStoreBinding(int FormatVersion, int KeyCodecVersion, Guid NodeId,
    Guid Incarnation, string SigningKeySha256, DurabilityProfile Durability, bool DispatchPaused,
    long ReadGeneration, long Position);

internal sealed record ReplicaUpgradeConfigurationBinding(string LocalId, ImmutableArray<string> VoterIds,
    string Directory, Guid Incarnation, bool BenchmarkTopology, int SnapshotThreshold,
    long LowerElectionTicks, long UpperElectionTicks, long HeartbeatTicks, long RpcTicks,
    int MaxAppendEntries, int MaxAppendBytes, int SnapshotChunkBytes, long MaxSnapshotBytes);
