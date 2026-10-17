using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record D2ResourceInventoryImportOwners(
    ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target,
    ControlledPartitionMovementLoopbackCorpus Corpus,
    ServerRuntimeOptions SourceRuntime,
    ServerRuntimeOptions TargetRuntime,
    PartitionMovementPeerAdmission SourceAdmission,
    PartitionMovementPeerAdmission TargetAdmission,
    string CallerAddress);
