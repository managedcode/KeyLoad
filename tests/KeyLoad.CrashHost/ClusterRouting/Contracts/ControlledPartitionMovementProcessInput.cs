using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledPartitionMovementProcessProtocol.InputAlias)]
internal sealed record ControlledPartitionMovementProcessInput(
    [property: global::Orleans.Id(0)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(1)] ReplicatedOperation OriginalOperation,
    [property: global::Orleans.Id(2)] long BeforePosition,
    [property: global::Orleans.Id(3)] long BeforeReplicaIndex,
    [property: global::Orleans.Id(4)] CommitStage FaultStage,
    [property: global::Orleans.Id(5)] long MaximumFixtureEntries);
