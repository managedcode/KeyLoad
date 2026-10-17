using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal sealed record ReplicaRetainedReplayCommand(ReplicatedOperation Operation, OperationResult Result);

internal sealed record ReplicaRetainedReplaySeed(ImmutableArray<ReplicaRetainedReplayCommand> Commands,
    BlobRef Blob, BlobMetadata Metadata);
