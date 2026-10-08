using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementProcessProtocol
{
    internal const string Prepare = "controlled-movement-prepare";
    internal const string Fault = "controlled-movement-fault";
    internal const string Recover = "controlled-movement-recover";
    internal const string Verify = "controlled-movement-verify";
    internal const string InputFile = "movement-phase-input.bin";
    internal const string RecoveredFile = "movement-phase-recovered.bin";
    internal const string VerifiedFile = "movement-phase-verified.bin";
    internal const string InputAlias = "keyload.crashhost.controlled-movement-process-input.v1";
    internal const string Invalid = "The original controlled movement process evidence is inconsistent.";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ControlledPartitionMovementProcessProtocol.InputAlias)]
internal sealed record ControlledPartitionMovementProcessInput(
    [property: global::Orleans.Id(0)] PhysicalShardRecord Owner,
    [property: global::Orleans.Id(1)] ReplicatedOperation OriginalOperation,
    [property: global::Orleans.Id(2)] long BeforePosition,
    [property: global::Orleans.Id(3)] long BeforeReplicaIndex,
    [property: global::Orleans.Id(4)] CommitStage FaultStage,
    [property: global::Orleans.Id(5)] long MaximumFixtureEntries);
