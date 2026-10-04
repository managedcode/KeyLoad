using KeyLoad.Replication;

namespace KeyLoad.Server;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ServerNodeUpgradeProtocol.PreparedAlias)]
internal sealed record ServerNodeUpgradeReceipt(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] string OriginalSource,
    [property: global::Orleans.Id(2)] string FinalDestination,
    [property: global::Orleans.Id(3)] string OriginalInventorySha256,
    [property: global::Orleans.Id(4)] string CanonicalSourceIdentitySha256,
    [property: global::Orleans.Id(5)] string CanonicalSourceJournalSha256,
    [property: global::Orleans.Id(6)] string ReplicaSourceIdentitySha256,
    [property: global::Orleans.Id(7)] string ReplicaSourceJournalSha256,
    [property: global::Orleans.Id(8)] Guid CanonicalNodeId,
    [property: global::Orleans.Id(9)] Guid ReplicaNodeId,
    [property: global::Orleans.Id(10)] Guid Incarnation,
    [property: global::Orleans.Id(11)] int SourceEpoch,
    [property: global::Orleans.Id(12)] int TargetEpoch,
    [property: global::Orleans.Id(13)] long CanonicalPosition,
    [property: global::Orleans.Id(14)] long CanonicalAppliedPosition,
    [property: global::Orleans.Id(15)] long ReplicaPhysicalPositionBefore,
    [property: global::Orleans.Id(16)] long CanonicalReadGeneration,
    [property: global::Orleans.Id(17)] long ReplicaReadGeneration,
    [property: global::Orleans.Id(18)] ReplicaHardState OriginalReplicaHardState,
    [property: global::Orleans.Id(19)] string PreparedTargetInventorySha256,
    [property: global::Orleans.Id(20)] int SourceBackupDirectoryCount);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ServerNodeUpgradeProtocol.OwnerAlias)]
internal sealed record ServerNodeUpgradeOwner(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] string OriginalSource,
    [property: global::Orleans.Id(2)] string FinalDestination,
    [property: global::Orleans.Id(3)] string OriginalInventorySha256,
    [property: global::Orleans.Id(4)] string CanonicalIdentitySha256,
    [property: global::Orleans.Id(5)] string CanonicalJournalSha256,
    [property: global::Orleans.Id(6)] string ReplicaIdentitySha256,
    [property: global::Orleans.Id(7)] string ReplicaJournalSha256,
    [property: global::Orleans.Id(8)] int SourceEpoch,
    [property: global::Orleans.Id(9)] int TargetEpoch);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ServerNodeUpgradeProtocol.EnvelopeAlias)]
internal sealed record ServerNodeUpgradeEnvelope(
    [property: global::Orleans.Id(0)] byte[] Payload,
    [property: global::Orleans.Id(1)] byte[] Sha256);

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ServerNodeUpgradeProtocol.ProgressAlias)]
internal sealed record ServerNodeUpgradeProgress(
    [property: global::Orleans.Id(0)] int FormatVersion,
    [property: global::Orleans.Id(1)] ServerNodeUpgradeOwner SourceOwner,
    [property: global::Orleans.Id(2)] string InventorySha256,
    [property: global::Orleans.Id(3)] int StageCode,
    [property: global::Orleans.Id(4)] int SourceEpoch,
    [property: global::Orleans.Id(5)] int TargetEpoch);
