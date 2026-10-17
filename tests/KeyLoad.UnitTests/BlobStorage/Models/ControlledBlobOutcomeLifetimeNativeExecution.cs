using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.BlobStorage;

internal sealed record ControlledBlobOutcomeLifetimeNativeExecution(ControlledPartitionMovementNode Source,
    ControlledPartitionMovementNode Target, ControlledPartitionMovementLoopbackListeners Listeners,
    ControlledPartitionMovementLoopbackCorpus Corpus, ServerRuntimeOptions SourceOptions,
    ServerRuntimeOptions TargetOptions);
