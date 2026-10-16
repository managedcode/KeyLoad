using System.Collections.Immutable;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record ControlledBlobOutcomeLifetimeNativeProof(ReplicatedOperation Original, OperationResult Expected);

internal sealed record ControlledBlobOutcomeLifetimeNativeState(ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target, ImmutableArray<ControlledBlobOutcomeLifetimeNativeProof> Proofs,
    RegisteredPhysicalOwnerV1 Destination, string CallerAddress);

internal sealed record ControlledBlobOutcomeLifetimeNativeExecution(ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target, ControlledPartitionMovementLoopbackListeners Listeners,
    ControlledPartitionMovementLoopbackCorpus Corpus, ServerRuntimeOptions SourceOptions,
    ServerRuntimeOptions TargetOptions);
